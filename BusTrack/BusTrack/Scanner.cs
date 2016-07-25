using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.Threading;
using BusTrack.Utilities;
using Android.Net.Wifi;
using System.Diagnostics;
using Android.Locations;
using Realms;
using BusTrack.Data;
using System.Collections.Specialized;
using Android.Util;

namespace BusTrack
{
    [Service]
    class Scanner : Service
    {
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
                    using(Realm realm = Realm.GetInstance(Utils.NAME_PREF))
                    {
                        string busAp = prefs.GetString("currentAp", null);
                        int travelId = prefs.GetInt("currentTravel", -1);
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

                                    Log.Debug(Utils.NAME_PREF, "Current bus is " + busAp);

                                    // Store last state
                                    ISharedPreferencesEditor editor = prefs.Edit();
                                    editor.PutInt("currentTravel", current.id);
                                    editor.PutString("currentAp", busAp);
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
                                    // User finished travel
                                    candidates[busAp].Item1.Reset();
                                    candidates.Clear();
                                    Stop nearest = FindNearestStop(end, realm);
                                    long distance = Utils.GetDistance(current.init, nearest);
                                    Log.Debug(Utils.NAME_PREF, "Travel finished");
                                    realm.Write(() =>
                                    {
                                        current.time = DateTimeOffset.Now.Subtract(current.date).Seconds;
                                        current.distance = distance;
                                        current.end = nearest;

                                        // If we know the line, check if it's necessary to update line and stop
                                        if (current.line != null && !nearest.lines.Contains(current.line))
                                        {
                                            if (!nearest.lines.Contains(current.line)) nearest.lines.Add(current.line);
                                            if (!current.line.stops.Contains(nearest)) current.line.stops.Add(nearest);
                                        }
                                    });

                                    // Reset variables
                                    busAp = null;
                                    current = null;

                                    // Reset last state
                                    ISharedPreferencesEditor editor = prefs.Edit();
                                    editor.PutInt("currentTravel", -1);
                                    editor.PutString("currentAp", null);
                                    editor.Apply();
                                }
                            }
                            realm.Refresh();
                        }
                    }
                } catch (ThreadInterruptedException e)
                {
                    // Ignore e
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
                realm.Write(() =>
                {
                    var stop  = realm.CreateObject<Stop>();
                    stop.location = current;
                    nearest = stop;
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
                if (ap.Bssid.Equals(busAp) && ap.Level >= -70)
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
            List<string> apNames = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private).GetStringSet(Utils.PREF_NETWORKS, new List<string>()).ToList();

            // Check if any network is being received
            foreach (ScanResult ap in wifi.Results)
            {
                if (apNames.Contains(ap.Ssid) && ap.Level > -70)
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
                if (candidates[cand].Item1.Elapsed.Seconds >= 5)
                {
                    // Bus caught!
                    busAp = cand;
                    Location location = candidates[cand].Item2;
                    candidates[cand].Item1.Reset();

                    // Search stop
                    Stop nearest = FindNearestStop(location, realm);

                    // Create travel object
                    realm.Write(() =>
                    {
                        // Get current bus
                        Bus currentBus;
                        string ap = busAp;
                        var buses = realm.All<Bus>().Where(b => b.mac == ap);
                        if (buses.Count() == 0)
                        {
                            // Not added yet to DB!
                            currentBus = realm.CreateObject<Bus>();
                            currentBus.mac = ap;
                        }
                        else currentBus = buses.First();

                        // Create new travel
                        current = realm.CreateObject<Travel>();
                        // TODO: Insert user id into travel object
                        current.date = DateTimeOffset.Now;
                        current.bus = currentBus;
                        current.init = nearest;
                        currentBus.travels.Add(current);
                    });

                    // Search which line owns the stop
                    var lines = nearest.lines;
                    if (lines.Count > 1 || lines.Count == 0)
                    {
                        // Ask user in which line is him
                        NotificationManager notificator = GetSystemService(NotificationService) as NotificationManager;
                        Notification.Builder builder = new Notification.Builder(Application.Context);
                        builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo);

                        builder.SetContentText("Por favor, introduce la línea en la que estás viajando.");
                        builder.SetContentTitle("Línea de bus no detectada");

                        builder.SetDefaults(NotificationDefaults.Sound | NotificationDefaults.Vibrate);
                        builder.SetAutoCancel(true);
                        builder.SetPriority((int)NotificationPriority.Max);

                        Intent opts = new Intent(Application.Context, typeof(MainActivity));
                        opts.PutExtra("travel", current.id);
                        if (lines.Count > 1)
                        {
                            List<int> ids = new List<int>();
                            foreach(Line l in lines)
                            {
                                ids.Add(l.id);
                            }
                            opts.PutExtra("lines", ids.ToArray());
                        }
                        PendingIntent wifiOpts = PendingIntent.GetActivity(Application.Context, 1, opts, PendingIntentFlags.OneShot);
                        builder.SetContentIntent(wifiOpts);

                        if ((int)Build.VERSION.SdkInt >= 21)
                        {
                            builder.SetCategory(Notification.CategoryError);
                            builder.SetVisibility(NotificationVisibility.Public);
                        }

                        Notification notif = builder.Build();
                        notificator.Notify("updateTravel", current.id, notif);
                    }
                    else
                    {
                        realm.Write(() =>
                        {
                            current.init = nearest;
                            current.line = lines.First();
                            // Bus outdated or new one created!
                            if ((current.bus.line != null && current.line != null && current.bus.line.id != current.line.id) || (current.bus.line == null && current.line != null))
                            {
                                current.bus.line = current.line;
                                current.bus.lastRefresh = DateTimeOffset.Now;
                            }
                        });
                        // TODO: Notify user if current line is correct (?)
                    }
                }
            }
            return current;
        }
    }
    #endregion functionality
}