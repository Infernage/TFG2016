using Android.App;
using Android.Content;
using Android.Locations;
using Android.Net.Wifi;
using Android.OS;
using Android.Runtime;
using BusTrack.Data;
using BusTrack.Utilities;
using Realms;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace BusTrack
{
    [Service]
    internal class ScannerService : Service
    {
        private readonly string currentAp = "currentAp";
        private readonly string currentTravel = "currentTravel";
        private Thread scanner;

        public override void OnCreate()
        {
            base.OnCreate();
            scanner = new Thread(() =>
            {
                AutoResetEvent evt = new AutoResetEvent(false);
                WifiUtility wifi = new WifiUtility(Application.Context, evt);
                LocationUtility location = new LocationUtility(Application.Context);
                ISharedPreferences prefs = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
                try
                {
                    using (Realm realm = Realm.GetInstance(Utils.GetDB()))
                    {
                        string busAp = prefs.GetString(currentAp, null);
                        long travelId = prefs.GetLong(currentTravel, -1);
                        var results = realm.All<Travel>().Where(t => t.id == travelId);
                        Travel current = results.Count() > 0 ? results.First() : null;
                        Dictionary<string, Tuple<Stopwatch, Location>> candidates = new Dictionary<string, Tuple<Stopwatch, Location>>();

                        if (current != null)
                        {
                            candidates.Add(busAp, Tuple.Create<Stopwatch, Location>(new Stopwatch(), null));
                        }
                        while (true)
                        {
                            wifi.StartScan();
                            evt.WaitOne();
                            if (current == null)
                            {
                                // Scan when user starts a travel
                                ScanUp(wifi, location, candidates);

                                // Search for a new travel
                                current = Search(realm, candidates);

                                // If we get a travel, start it
                                if (current != null)
                                {
                                    busAp = current.bus.mac;

                                    // Store last state
                                    ISharedPreferencesEditor editor = prefs.Edit();
                                    editor.PutLong(currentTravel, current.id);
                                    editor.PutString(currentAp, busAp);
                                    editor.Apply();

                                    candidates.Clear();
                                    candidates.Add(busAp, Tuple.Create<Stopwatch, Location>(new Stopwatch(), null));
                                }
                            }
                            else
                            {
                                Location end = null;

                                // Check if user stops the current travel
                                if (ScanDown(wifi, location, busAp, candidates, out end))
                                {
                                    NotificationManager notificator = GetSystemService(NotificationService) as NotificationManager;
                                    notificator.Cancel("correctTravel", (int)current.id);

                                    // User finished travel
                                    candidates[busAp].Item1.Reset();
                                    candidates.Clear();
                                    Stop nearest = FindNearestStop(end, realm);
                                    long distance = GoogleUtils.GetDistance(current.init, nearest);
                                    Bus bus = current.bus;
                                    realm.Write(() =>
                                    {
                                        if (bus.lineId != current.line?.id && current.line != null)
                                        {
                                            bus.line = current.line;
                                            bus.lineId = current.line.id;
                                            bus.lastRefresh = DateTime.Now;
                                            if (RestUtils.UpdateBus(this, bus).Result) bus.synced = true;
                                        }
                                        current.time = DateTimeOffset.Now.Subtract(current.date).Seconds;
                                        current.distance = distance;
                                        current.end = nearest;

                                        // If we know the line, check if it's necessary to update line and stop
                                        if (current.line != null && !nearest.lines.Contains(current.line))
                                        {
                                            RestUtils.UpdateLineStop(this, current.line, nearest).Wait();
                                            if (!nearest.lines.Contains(current.line)) nearest.lines.Add(current.line);
                                            if (!current.line.stops.Contains(nearest)) current.line.stops.Add(nearest);
                                        }
                                    });

                                    // If auto sync is enabled, upload travel to server
                                    if (prefs.GetBoolean("autoSync" + prefs.GetLong(Utils.PREF_USER_ID, -1).ToString(), true))
                                    {
                                        // If upload was OK, mark as synced
                                        if (RestUtils.UploadTravel(this, current).Result) realm.Write(() => current.synced = true);
                                    }

                                    // Reset variables
                                    busAp = null;
                                    current = null;

                                    // Reset last state
                                    ISharedPreferencesEditor editor = prefs.Edit();
                                    editor.Remove(currentTravel);
                                    editor.Remove(currentAp);
                                    editor.Apply();

                                    // Sync DB
                                    RestUtils.Sync(this);
                                }
                            }
                            realm.Refresh();
                        }
                    }
                }
                catch (ThreadInterruptedException)
                {
                    // Ignore exception
                    location.Disconnect();
                }
            });
            scanner.IsBackground = false;
            scanner.Name = "BusTrackScanner";
            scanner.Start();
        }

        [return: GeneratedEnum]
        public override StartCommandResult OnStartCommand(Intent intent, [GeneratedEnum] StartCommandFlags flags, int startId)
        {
            return StartCommandResult.Sticky; // Always sticky!
        }

        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            scanner.Interrupt();
            scanner.Join(100);
        }

        #region functionality

        /// <summary>
        /// Gets the nearest stop of the given location.
        /// </summary>
        /// <param name="current">The current location.</param>
        /// <param name="realm">The DB controller. Used in the case no stop is found, in that case, we create it.</param>
        /// <returns></returns>
        private Stop FindNearestStop(Location current, Realm realm)
        {
            var stops = realm.All<Stop>();
            OrderedDictionary nearestStops = new OrderedDictionary(); // Ordered in distance!
            foreach (Stop s in stops)
            {
                float distance = current.DistanceTo(s.location);
                if (distance <= 6F) nearestStops.Add(distance, s);
            }
            Stop nearest = null;
            if (nearestStops.Count == 0)
            {
                // No stored stop! Create new one
                nearest = RestUtils.CreateStop(this, current).Result;
                realm.Write(() =>
                {
                    if (!nearest.synced) nearest.GenerateID(realm);
                    realm.Manage(nearest);
                });
            }
            else nearest = nearestStops[0] as Stop;
            return nearest;
        }

        /// <summary>
        /// Checks whether the user gets off the bus.
        /// </summary>
        /// <param name="wifi">WifiUtility</param>
        /// <param name="location">LocationUtility</param>
        /// <param name="busAp">The wifi bus name</param>
        /// <param name="candidates">A list with just 1 element (in this case).</param>
        /// <param name="travelEnd">The location where travel ends.</param>
        /// <returns></returns>
        private bool ScanDown(WifiUtility wifi, LocationUtility location, string busAp, Dictionary<string, Tuple<Stopwatch, Location>> candidates, out Location travelEnd)
        {
            Location end = null;
            bool notFound = true;
            foreach (ScanResult ap in wifi.Results)
            {
                if (ap.Bssid.Equals(busAp) && ap.Level <= (Utils.GetNetworkDetection(this).Item2 * -1))
                {
                    // User still travelling
                    notFound = false;
                    if (candidates[busAp].Item1.IsRunning)
                    {
                        candidates[busAp].Item1.Reset();
                        end = null;
                    }
                }
                else if (ap.Bssid.Equals(busAp))
                {
                    // Network is too far away! User finished travel?
                    if (!candidates[busAp].Item1.IsRunning)
                    {
                        candidates[busAp].Item1.Start();
                        end = location.LastLocation;
                    }
                }
            }

            // Just in case wifi.Results doesn't contain busAp
            if (notFound && !candidates[busAp].Item1.IsRunning)
            {
                candidates[busAp].Item1.Start();
                end = location.LastLocation;
            }

            if (notFound && candidates[busAp].Item1.Elapsed.Seconds >= 3) end = location.LastLocation;

            travelEnd = end;
            return notFound;
        }

        /// <summary>
        /// Checks if the user gets on a bus.
        /// </summary>
        /// <param name="wifi">WifiUtility</param>
        /// <param name="location">LocationUtility</param>
        /// <param name="candidates">A list of candidate networks being scanned.</param>
        private void ScanUp(WifiUtility wifi, LocationUtility location, Dictionary<string, Tuple<Stopwatch, Location>> candidates)
        {
            // Get stored network names
            List<string> apNames = Utils.GetNetworks(this);

            // Check if any network is being received
            foreach (ScanResult ap in wifi.Results)
            {
                if (apNames.Contains(ap.Ssid) && ap.Level > (Utils.GetNetworkDetection(this).Item1 * -1))
                {
                    // New possible network!
                    if (!candidates.Keys.Contains(ap.Bssid)) candidates.Add(ap.Bssid, Tuple.Create(Stopwatch.StartNew(), location.LastLocation));
                }
                else if (apNames.Contains(ap.Ssid))
                {
                    // Network too far away, if we stored as a candidate, remove it!
                    foreach (string cand in candidates.Keys)
                    {
                        if (cand.Equals(ap.Bssid))
                        {
                            candidates[cand].Item1.Stop();
                            candidates.Remove(cand);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the user has started a travel.
        /// </summary>
        /// <param name="realm">The DB controller. Needed to create a Travel object.</param>
        /// <param name="candidates">A list of candidate networks. Got from ScanUp method.</param>
        /// <returns></returns>
        private Travel Search(Realm realm, Dictionary<string, Tuple<Stopwatch, Location>> candidates)
        {
            Travel current = null;
            string busAp;

            foreach (string cand in candidates.Keys)
            {
                if (candidates[cand].Item1.Elapsed.Seconds >= Utils.GetTimeTravelDetection(this))
                {
                    // Bus caught!
                    busAp = cand;
                    Location location = candidates[cand].Item2;
                    candidates[cand].Item1.Reset();

                    // Search stop
                    Stop nearest = FindNearestStop(location, realm);

                    // Create/Get bus
                    var buses = realm.All<Bus>().Where(b => b.mac == busAp);
                    Bus bus = buses.Any() ? buses.First() : RestUtils.CreateBus(this, new Bus { lastRefresh = DateTime.Now, mac = busAp }).Result;

                    // Create travel object
                    realm.Write(() =>
                    {
                        if (!buses.Any()) realm.Manage(bus);
                        // Create new travel
                        current = realm.CreateObject<Travel>();
                        ISharedPreferences prefs = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
                        current.userId = prefs.GetLong(Utils.PREF_USER_ID, -1);
                        current.date = DateTimeOffset.Now;
                        current.bus = bus;
                        current.init = nearest;
                        current.GenerateID(realm);
                    });

                    // Search which line owns the stop
                    var lines = nearest.lines;
                    NotificationManager notificator = GetSystemService(NotificationService) as NotificationManager;
                    Notification.Builder builder = new Notification.Builder(Application.Context);
                    builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo);
                    builder.SetOngoing(true); // Avoid user to cancel notification

                    builder.SetDefaults(NotificationDefaults.Sound | NotificationDefaults.Vibrate);

                    Intent opts = new Intent(Application.Context, typeof(MainActivity));
                    opts.PutExtra("travel", current.id);
                    PendingIntent wifiOpts = PendingIntent.GetActivity(Application.Context, 1, opts, PendingIntentFlags.OneShot);
                    builder.SetContentIntent(wifiOpts);

                    if ((int)Build.VERSION.SdkInt >= 21)
                    {
                        builder.SetCategory(Notification.CategoryEvent);
                        builder.SetVisibility(NotificationVisibility.Public);
                    }
                    if (lines.Count > 1 || lines.Count == 0)
                    {
                        // Ask user in which line is him
                        builder.SetContentText("Por favor, introduce la línea en la que estás viajando.");
                        builder.SetContentTitle("Línea de bus no detectada");

                        builder.SetAutoCancel(true);
                        builder.SetPriority((int)NotificationPriority.Max);

                        if (lines.Count > 1)
                        {
                            List<long> ids = new List<long>();
                            foreach (Line l in lines)
                            {
                                ids.Add(l.id);
                            }
                            opts.PutExtra("lines", ids.ToArray());
                        }

                        Notification notif = builder.Build();
                        notificator.Notify("updateTravel", (int)current.id, notif);
                    }
                    else
                    {
                        builder.SetContentText("Viajando en la línea " + lines.First().id.ToString() + ". Pulsa para cambiar.");
                        builder.SetContentTitle("Viaje detectado");
                        builder.SetAutoCancel(false);

                        Notification notif = builder.Build();
                        notificator.Notify("correctTravel", (int)current.id, notif);
                        realm.Write(() =>
                        {
                            current.line = lines.First();
                            // Bus outdated or new one created!
                            if ((bus.line != null && current.line != null && bus.line.id != current.line.id) || (bus.line == null && current.line != null))
                            {
                                bus.line = current.line;
                                bus.lineId = current.line.id;
                                bus.lastRefresh = DateTimeOffset.Now;
                                if (RestUtils.UpdateBus(this, bus).Result) bus.synced = true;
                            }
                        });
                    }
                }
            }
            return current;
        }
    }

    #endregion functionality
}