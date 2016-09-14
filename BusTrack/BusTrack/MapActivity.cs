using Android.App;
using Android.Content;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.Locations;
using Android.OS;
using Android.Views;
using Android.Widget;
using BusTrack.Data;
using BusTrack.Utilities;
using Realms;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrack
{
    [Activity(Label = "Mapa")]
    public class MapActivity : Activity, IOnMapReadyCallback
    {
        private GoogleMap map = null;
        private Dictionary<long, Data> cache;
        private long selected = -1;

        public void OnMapReady(GoogleMap googleMap)
        {
            FindViewById<LinearLayout>(Resource.Id.travelsLayout).Enabled = true;
            map = googleMap;
            googleMap.AnimateCamera(CameraUpdateFactory.NewLatLngZoom(new LatLng(36.7212487, -4.4213463), 15));
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            map = null;
            FindViewById<Button>(Resource.Id.detButton).Click -= ShowDetails;
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);

            SetContentView(Resource.Layout.Map);
            cache = new Dictionary<long, Data>();

            LinearLayout layout = FindViewById<LinearLayout>(Resource.Id.travelsLayout);
            layout.Enabled = false;

            MenuInitializer.InitMenu(this);

            MapFragment mapFrag = FragmentManager.FindFragmentById<MapFragment>(Resource.Id.map);
            mapFrag.GetMapAsync(this);

            FindViewById<Button>(Resource.Id.detButton).Click += ShowDetails;

            using (Realm realm = Realm.GetInstance(Utils.GetDB()))
            {
                long id = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private).GetLong(Utils.PREF_USER_ID, -1);
                var query = realm.All<Travel>().Where(t => t.userId == id);
                query = query.OrderByDescending(t => t.date);

                // No trips in our DB associated with the user... RIP
                if (!query.Any())
                {
                    TextView text = new TextView(this);
                    text.Text = "No existen viajes recientes";
                    layout.AddView(text);
                    return;
                }

                int n = 10; // Max trips!
                foreach (Travel travel in query)
                {
                    if (travel.end == null && travel.time == 0) continue; // Unfinished travel

                    long tid = travel.id;
                    Location init = travel.init.location, end = travel.end.location;

                    Button button = new Button(this);
                    button.Text = travel.date.ToString("G", CultureInfo.CurrentCulture.DateTimeFormat);
                    button.Click += async (o, e) =>
                    {
                        selected = tid;

                        if (cache.ContainsKey(tid)) Update(cache[tid]); // Already cached, not needed to send a request
                        else
                        {
                            button.Enabled = false;
                            await Task.Run(() =>
                            {
                                // Do this in a separate thread (Web request)
                                using (Realm realmClick = Realm.GetInstance(Utils.GetDB()))
                                {
                                    PolylineOptions opts = GoogleUtils.GetRoute(realmClick.All<Travel>().Where(t => t.id == tid).First());
                                    RunOnUiThread(() => // Modify UI, do it in the correct thread
                                    {
                                        button.Enabled = true;
                                        Data d = new Data
                                        {
                                            opts = opts,
                                            end = end,
                                            init = init
                                        };
                                        cache.Add(tid, d);
                                        Update(d);
                                    });
                                }
                            });
                        }
                    };

                    // Don't forget to add the button
                    layout.AddView(button);

                    // No more trips!
                    if (n-- == 0) break;
                }
            }
        }

        /// <summary>
        /// Called when Show details button is pressed.
        /// </summary>
        /// <param name="sender">Sender object (this, in this case).</param>
        /// <param name="e">Event args sent.</param>
        private void ShowDetails(object sender, EventArgs e)
        {
            if (selected == -1)
            {
                Toast.MakeText(this, "Selecciona un viaje", ToastLength.Short).Show();
                return;
            }
            FragmentTransaction trans = FragmentManager.BeginTransaction();
            Fragment prev = FragmentManager.FindFragmentByTag("details");
            if (prev != null) trans.Remove(prev);
            trans.AddToBackStack(null);
            new DetailsDialog(this, selected).Show(trans, "details");
        }

        /// <summary>
        /// Updates the map with the data provided.
        /// </summary>
        /// <param name="data">A structure with the route, initial and final stops.</param>
        private void Update(Data data)
        {
            if (map != null)
            {
                // Clear and add data
                map.Clear();
                map.AddPolyline(data.opts);

                MarkerOptions iopts = new MarkerOptions();
                iopts.SetPosition(new LatLng(data.init.Latitude, data.init.Longitude));
                iopts.SetTitle("Inicio");
                iopts.SetIcon(BitmapDescriptorFactory.DefaultMarker(BitmapDescriptorFactory.HueCyan));
                map.AddMarker(iopts);

                MarkerOptions eopts = new MarkerOptions();
                eopts.SetPosition(new LatLng(data.end.Latitude, data.end.Longitude));
                eopts.SetTitle("Fin");
                eopts.SetIcon(BitmapDescriptorFactory.DefaultMarker(BitmapDescriptorFactory.HueCyan));
                map.AddMarker(eopts);

                // Update camera for new data
                IList<LatLng> points = data.opts.Points;
                var builder = new LatLngBounds.Builder();
                foreach (LatLng p in points) builder.Include(p);

                LatLngBounds bounds = builder.Include(iopts.Position).Include(eopts.Position).Build();

                CameraUpdate cu = CameraUpdateFactory.NewLatLngBounds(bounds, 50);
                map.AnimateCamera(cu);
            }
        }

        private struct Data
        {
            public PolylineOptions opts;
            public Location init;
            public Location end;
        }
    }

    /// <summary>
    /// Class used to display the travel details dialog.
    /// </summary>
    internal class DetailsDialog : DialogFragment
    {
        private long travel;
        private Context context;

        public DetailsDialog()
        {
            travel = -1;
        }

        public DetailsDialog(Context c, long travelId)
        {
            context = c;
            travel = travelId;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            if (travel == -1) return base.OnCreateDialog(savedInstanceState);

            using (Realm realm = Realm.GetInstance(Utils.GetDB()))
            {
                Travel t = realm.All<Travel>().Where(tr => tr.id == travel).First();

                // Create dialog
                AlertDialog dialog = null;
                var builder = new AlertDialog.Builder(Activity);
                builder.SetView(Resource.Layout.DetailsLayout);
                builder.SetPositiveButton("Aceptar", (o, e) => dialog.Dismiss());

                dialog = builder.Create();
                dialog.Show();

                EditText distance = dialog.FindViewById<EditText>(Resource.Id.distanceF),
                    duration = dialog.FindViewById<EditText>(Resource.Id.durationF),
                    date = dialog.FindViewById<EditText>(Resource.Id.dateF),
                    line = dialog.FindViewById<EditText>(Resource.Id.lineF);

                // Update UI
                distance.Text = (t.distance >= 1000 ? Math.Round(t.distance / 1000F, 2) : t.distance).ToString() + (t.distance >= 1000 ? " Km" : " metros");
                duration.Text = (t.time >= 3600 ? Math.Round(t.time / 3600F, 2) : Math.Round(t.time / 60F, 2)).ToString() + (t.time >= 3600 ? " horas" : " minutos");
                date.Text = t.date.ToString("F", CultureInfo.CurrentCulture);
                line.Text = t.line.id + " " + t.line.name;

                return dialog;
            }
        }
    }
}