using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Graphics.Drawables;
using Android.Graphics;
using Android.Util;
using Realms;
using BusTrack.Utilities;
using BusTrack.Data;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using Android.Locations;
using System.Collections.Generic;

namespace BusTrack
{
    [Activity(Label = "BusTrack", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {

        protected override void OnCreate(Bundle bundle)
        {
            CheckDBIntegrity();
            base.OnCreate(bundle);
            RequestWindowFeature(WindowFeatures.NoTitle);
            //ActionBar.SetBackgroundDrawable(new ColorDrawable(Color.LightBlue));

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            // Load shared prefs
            ISharedPreferences prefs = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            List<string> apNames = prefs.GetStringSet(Utils.PREF_NETWORKS, new List<string>()).ToList();
            if (apNames.Count == 0)
            {
                apNames.Add("wifibus");
                ISharedPreferencesEditor edit = prefs.Edit();
                edit.PutStringSet(Utils.PREF_NETWORKS, apNames);
                edit.Apply();
            }

            // Initialize ListView
            NetworkListAdapter adapter = new NetworkListAdapter(this);
            ListView view = FindViewById<ListView>(Resource.Id.listView1);
            view.Adapter = adapter;

            MenuInitializer.InitMenu(this);

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

            Intent service = new Intent(this, typeof(Scanner));
            service.AddFlags(ActivityFlags.NewTask);
            StartService(service);
        }

        /// <summary>
        /// Checks if DB is filled with, at least, JSON data.
        /// </summary>
        private void CheckDBIntegrity()
        {
            RealmConfiguration config = new RealmConfiguration(Utils.NAME_PREF, false);
            using (Realm realm = Realm.GetInstance(config))
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
                int linesSize = (int)stopsLines["linesSize"];
                int stopsSize = (int)stopsLines["stopsSize"];
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
                            loc.Latitude = float.Parse(location[0]);
                            loc.Longitude = float.Parse(location[1]);
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

                            foreach (int id in line.stopIds)
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

            Realm realm = Realm.GetInstance(Utils.NAME_PREF);

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

            dialog.Show(); // Just in case!

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

            Realm realm = Realm.GetInstance(Utils.NAME_PREF);

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
                    realm.Dispose();
                    dialog.Dismiss();
                    activity.Finish();
                }
            };

            return dialog;
        }
    }

    class NetworkListAdapter : BaseAdapter<string>
    {
        private ISharedPreferences prefs;
        private List<string> list;
        private Activity context;

        public NetworkListAdapter(Activity c)
        {
            context = c;
            prefs = c.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            list = prefs.GetStringSet(Utils.PREF_NETWORKS, new List<string>()).ToList();
        }

        public override string this[int position]
        {
            get
            {
                return list[position];
            }
        }

        public override int Count
        {
            get
            {
                return list.Count;
            }
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            var view = convertView;
            if (view == null)
            {
                view = context.LayoutInflater.Inflate(Resource.Layout.TextViewAdapter, parent, false);
                Button button = view.FindViewById<Button>(Resource.Id.button1);
                button.Click += (o, e) =>
                {
                    AlertDialog dialog = null;
                    AlertDialog.Builder builder = new AlertDialog.Builder(context);
                    builder.SetMessage("¿Estás seguro de que quieres eliminar la red seleccionada?");
                    builder.SetPositiveButton("Sí", (ob, ev) =>
                    {
                        dialog.Dismiss();
                        list.RemoveAt(position);
                        ISharedPreferencesEditor edit = prefs.Edit();
                        edit.PutStringSet(Utils.PREF_NETWORKS, list);
                        context.RunOnUiThread(() => NotifyDataSetChanged());
                    });
                    builder.SetNegativeButton("No", (ob, ev) => dialog.Dismiss());

                    dialog = builder.Create();
                    dialog.Show();
                };
            }

            view.FindViewById<TextView>(Resource.Id.textViewAdapter).Text = list[position];

            return view;
        }

        public void Add(string value)
        {
            list.Add(value);
            ISharedPreferencesEditor edit = prefs.Edit();
            edit.PutStringSet(Utils.PREF_NETWORKS, list);
            edit.Apply();
            context.RunOnUiThread(() => NotifyDataSetChanged());
        }
    }
}

