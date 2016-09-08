using System;
using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Android.OS;
using Realms;
using BusTrack.Data;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using Android.Locations;
using System.Collections.Generic;
using BusTrack.Utilities;
using Android.Net.Wifi;
using Android.Graphics;
using Android.Content.Res;

namespace BusTrack
{
    [Activity(Label = "BusTrack", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        private NetworkListAdapter adapter;

        protected async override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            if (!await Utils.CheckLogin(this))
            {
                StartActivity(typeof(LoginActivity));
                Finish();
                return;
            }
            CheckDBIntegrity();
            RequestWindowFeature(WindowFeatures.NoTitle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            // Load shared prefs
            ISharedPreferences prefs = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);

            // Load networks names
            List<string> apNames = Utils.GetNetworks(this);
            if (apNames.Count == 0)
            {
                apNames.Add("wifibus");
                Utils.SetNetworks(this, apNames);
            }

            // Initialize ListView
            adapter = new NetworkListAdapter(this);
            ListView view = FindViewById<ListView>(Resource.Id.listView1);
            view.Adapter = adapter;

            MenuInitializer.InitMenu(this);

            // Set user name in welcome message
            TextView welcome = FindViewById<TextView>(Resource.Id.welcome);
            string text = welcome.Text;
            welcome.Text = text.Replace("<user>", prefs.GetString(Utils.PREF_USER_NAME, "Usuario"));

            // Add network functionality
            Button addNetwork = FindViewById<Button>(Resource.Id.addNetwork);
            addNetwork.Click += (o, e) =>
            {
                AlertDialog dialog = null;
                AlertDialog.Builder builder = new AlertDialog.Builder(this);
                builder.SetTitle("Nombre de la red");

                EditText input = new EditText(this);
                input.InputType = Android.Text.InputTypes.ClassText;
                builder.SetView(input);

                builder.SetPositiveButton("Aceptar", (ob, ev) =>
                {
                    if (input.Text.Length == 0) return;
                    adapter.Add(input.Text);
                });

                builder.SetNegativeButton("Cancelar", (ob, ev) =>
                {
                    dialog.Dismiss();
                });

                dialog = builder.Create();
                dialog.Show();
            };

            int travelId = Intent.GetIntExtra("travel", -1);
            if (travelId != -1) // Activity created from a notification in a middle of a travel
            {
                int[] lines = Intent.GetIntArrayExtra("lines");
                string tag = lines != null ? Utils.NAME_LCHOOSER : Utils.NAME_LCREATOR;

                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag(tag);
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                if (lines != null) new LineChooserDialog(this, travelId, lines).Show(trans, tag); // We know the possible lines
                else new LineCreatorDialog(this, travelId).Show(trans, tag); // We don't know the possible lines
            }

            WifiUtility.UpdateNetworks += UpdateUI;

            // We are logged in, start scanner
            Intent service = new Intent(this, typeof(Scanner));
            service.AddFlags(ActivityFlags.NewTask);
            StartService(service);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            WifiUtility.UpdateNetworks -= UpdateUI;
        }

        /// <summary>
        /// Event receiver of WifiUtility.
        /// </summary>
        /// <param name="networks">The networks detected.</param>
        private void UpdateUI(List<ScanResult> networks)
        {
            List<string> stored = Utils.GetNetworks(this);
            List<string> nets = new List<string>();

            foreach(ScanResult res in networks)
            {
                if (!stored.Contains(res.Ssid)) nets.Add(res.Ssid);
            }

            adapter?.UpdateDetected(nets);
        }

        /// <summary>
        /// Checks if DB is filled with, at least, JSON data.
        /// </summary>
        private void CheckDBIntegrity()
        {
            using (Realm realm = Realm.GetInstance(Utils.GetDB(this)))
            {
                var stops = realm.All<Stop>();
                var lines = realm.All<Line>();
                string json;

                // Read JSON file
                using (StreamReader sr = new StreamReader(Assets.Open("emt.json")))
                {
                    json = sr.ReadToEnd();
                }
                var stopsLines = JObject.Parse(json);

                // Get each list size
                long linesSize = stopsLines["linesSize"].ToObject<long>();
                long stopsSize = stopsLines["stopsSize"].ToObject<long>();
                if (stopsSize > stops.Count())
                {
                    // Stop list is outdated!
                    realm.Write(() =>
                    {
                        var jStops = stopsLines["stops"].Children();
                        foreach (JToken token in jStops)
                        {
                            Stop stop = token.ToObject<Stop>();
                            Stop newStop = realm.CreateObject<Stop>();
                            newStop.id = stop.id;
                            string[] location = stop.position.Split('&');
                            Location loc = new Location("");
                            loc.Latitude = double.Parse(location[0]);
                            loc.Longitude = double.Parse(location[1]);
                            newStop.location = loc;
                        }
                    });
                }
                if (linesSize > lines.Count())
                {
                    // Line list is outdated!
                    realm.Write(() =>
                    {
                        var jLines = stopsLines["lines"].Children();
                        foreach (JToken token in jLines)
                        {
                            Line line = token.ToObject<Line>();
                            Line newLine = realm.CreateObject<Line>();
                            newLine.id = line.id;
                            newLine.name = line.name;

                            foreach (long id in line.stopIds)
                            {
                                // Get each stop
                                Stop s = (from stop in realm.All<Stop>()
                                          where stop.id == id
                                          select stop).First();

                                // Add every stop to each line and viceversa
                                newLine.stops.Add(s);
                                s.lines.Add(newLine);
                            }
                        }
                    });
                }
            }
        }
    }

    /// <summary>
    /// Class used to display a dialog to choose bus line
    /// </summary>
    class LineChooserDialog : DialogFragment
    {
        private int travel;
        private int[] lines;
        private Activity activity;

        public LineChooserDialog()
        {
            travel = -1;
        }

        public LineChooserDialog(Activity a, int travelId, int[] lines)
        {
            activity = a;
            this.lines = lines;
            travel = travelId;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            if (travel == -1) return base.OnCreateDialog(savedInstanceState);

            using (Realm realm = Realm.GetInstance(Utils.GetDB(activity)))
            {
                // Get objects from DB
                Travel t = realm.All<Travel>().Where(tr => tr.id == travel).First();
                List<Line> realmLines = new List<Line>();
                foreach (int l in lines)
                {
                    var results = from li in realm.All<Line>() where li.id == l select li;
                    realmLines.Add(results.First());
                }

                // Create dialog
                AlertDialog dialog = null;
                var builder = new AlertDialog.Builder(Activity);
                builder.SetView(Resource.Layout.LineChooser);
                builder.SetMessage("Línea tomada");
                builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);

                dialog = builder.Create();
                dialog.Show();

                Button accept = dialog.GetButton((int)DialogButtonType.Positive);
                accept.Click += (o, e) =>
                {
                    // Get line from radio button
                    Line line = null;
                    foreach (Line l in realmLines)
                    {
                        RadioButton radio = dialog.FindViewById<RadioButton>(l.id);
                        if (radio.Checked)
                        {
                            line = l;
                            break;
                        }
                    }

                    // Update travel with line selected
                    if (line != null)
                    {
                        realm.Write(() =>
                        {
                            t.line = line;

                            if ((t.bus.line != null && t.bus.line.id != t.line.id) || (t.bus.line == null))
                            {
                                t.bus.line = t.line;
                                t.bus.lastRefresh = DateTimeOffset.Now;
                            }

                            if (t.init != null)
                            {
                                if (!t.init.lines.Contains(line)) t.init.lines.Add(line);
                                if (!line.stops.Contains(t.init)) line.stops.Add(t.init);
                            }

                            if (t.end != null)
                            {
                                if (!t.end.lines.Contains(line)) t.end.lines.Add(line);
                                if (!line.stops.Contains(t.end)) line.stops.Add(t.end);
                            }
                        });
                        Dismiss();
                        activity.Finish();
                    }
                };

                // Set radio buttons
                RadioGroup group = dialog.FindViewById<RadioGroup>(Resource.Id.radioGroup1);
                foreach (Line l in realmLines)
                {
                    RadioButton button = new RadioButton(Activity);
                    button.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                    button.SetText(l.id.ToString() + " - " + l.name, TextView.BufferType.Normal);
                    button.Id = l.id;
                    group.AddView(button);
                }

                return dialog;
            }
        }
    }

    /// <summary>
    /// Class used to display a dialog to create a new bus line
    /// </summary>
    class LineCreatorDialog : DialogFragment
    {
        private int travel;
        private Activity activity;

        public LineCreatorDialog()
        {
            travel = -1;
        }

        public LineCreatorDialog(Activity a, int travelId)
        {
            activity = a;
            travel = travelId;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            if (travel == -1) return base.OnCreateDialog(savedInstanceState);

            using (Realm realm = Realm.GetInstance(Utils.GetDB(activity)))
            {
                Travel t = realm.All<Travel>().Where(tr => tr.id == travel).First();

                // Create dialog
                AlertDialog dialog = null;
                var builder = new AlertDialog.Builder(Activity);
                builder.SetView(Resource.Layout.LineCreator);
                builder.SetMessage("Línea tomada");
                builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);

                dialog = builder.Create();

                dialog.Show(); // Just in case!

                Button accept = dialog.GetButton((int)DialogButtonType.Positive);
                accept.Click += (o, e) =>
                {
                    EditText number = dialog.FindViewById<EditText>(Resource.Id.editText1), name = dialog.FindViewById<EditText>(Resource.Id.editText2);

                    // Check if the user has entered at least 1 character
                    if (name.Text.Length != 0)
                    {
                        // Parse line number (if exists)
                        int lineNumber = -1;
                        int.TryParse(number.Text, out lineNumber);

                        var lines = realm.All<Line>().Where(l => l.id == lineNumber);

                        realm.Write(() =>
                        {
                            Line line = lines.Count() > 0 ? lines.First() : realm.CreateObject<Line>();
                            if (lines.Count() == 0)
                            {
                                if (lineNumber != -1) line.id = lineNumber;
                                string n = name.Text;
                                line.name = n;
                            }
                            t.line = line;

                            if ((t.bus.line != null && t.bus.line.id != t.line.id) || (t.bus.line == null))
                            {
                                t.bus.line = t.line;
                                t.bus.lastRefresh = DateTimeOffset.Now;
                            }

                            if (t.init != null)
                            {
                                if (!t.init.lines.Contains(line)) t.init.lines.Add(line);
                                if (!line.stops.Contains(t.init)) line.stops.Add(t.init);
                            }

                            if (t.end != null)
                            {
                                if (!t.end.lines.Contains(line)) t.end.lines.Add(line);
                                if (!line.stops.Contains(t.end)) line.stops.Add(t.end);
                            }
                        });
                        dialog.Dismiss();
                        activity.Finish();
                    }
                };

                return dialog;
            }
        }
    }

    class NetworkListAdapter : BaseAdapter<string>
    {
        private List<string> stored, detected;
        private Activity context;

        public NetworkListAdapter(Activity c)
        {
            context = c;
            stored = Utils.GetNetworks(c);
            detected = new List<string>();
        }

        public override string this[int position]
        {
            get
            {
                return position >= stored.Count ? detected[position - stored.Count] : stored[position];
            }
        }

        public override int Count
        {
            get
            {
                return stored.Count + detected.Count;
            }
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            // TODO: Find a way to sort items correctly
            var view = convertView;
            Button button;
            if (view == null)
            {
                view = context.LayoutInflater.Inflate(Resource.Layout.TextViewAdapter, parent, false);
                button = view.FindViewById<Button>(Resource.Id.button1);
                button.Click += (o, e) =>
                {
                    int npos = position;
                    if (IsStored(npos))
                    {
                        AlertDialog dialog = null;
                        AlertDialog.Builder builder = new AlertDialog.Builder(context);
                        builder.SetMessage("¿Estás seguro de que quieres eliminar la red seleccionada?");
                        builder.SetPositiveButton("Sí", (ob, ev) =>
                        {
                            dialog.Dismiss();
                            RemoveAt(npos);
                            Utils.SetNetworks(context, stored);
                            context.RunOnUiThread(() => NotifyDataSetChanged());
                            button.Text = "Añadir";
                            button.SetBackgroundColor(Color.LightBlue);
                        });
                        builder.SetNegativeButton("No", (ob, ev) => dialog.Dismiss());

                        dialog = builder.Create();
                        dialog.Show();
                    }
                    else
                    {
                        Add(this[npos]);
                        button.Text = "Eliminar";
                        button.SetBackgroundColor(Color.Red);
                    }
                };
            } else button = view.FindViewById<Button>(Resource.Id.button1);

            view.FindViewById<TextView>(Resource.Id.textViewAdapter).Text = this[position];
            if (IsStored(position))
            {
                button.Text = "Eliminar";
                button.SetBackgroundColor(Color.Red);
            }
            else
            {
                button.Text = "Añadir";
                button.SetBackgroundColor(Color.LightBlue);
            }

            return view;
        }

        /// <summary>
        /// Updates the detected networks.
        /// </summary>
        /// <param name="values">The network list.</param>
        internal void UpdateDetected(List<string> values)
        {
            if (values == null) return;

            detected = values;
            context.RunOnUiThread(() => NotifyDataSetChanged());
        }

        internal void Add(string value)
        {
            stored.Add(value);
            if (detected.Contains(value)) detected.Remove(value);
            Utils.SetNetworks(context, stored);
            context.RunOnUiThread(() => NotifyDataSetChanged());
        }

        private bool IsStored(int position)
        {
            return position < stored.Count;
        }

        private void RemoveAt(int position)
        {
            if (IsStored(position)) stored.RemoveAt(position);
            else detected.RemoveAt(position);
        }
    }
}

