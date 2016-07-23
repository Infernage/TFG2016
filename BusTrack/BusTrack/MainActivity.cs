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

            MenuInitializer.InitMenu(this);

            int travelId = Intent.GetIntExtra("travel", -1);
            if (travelId != -1) // Activity created from a notification in a middle of a travel
            {
                int[] lines = Intent.GetIntArrayExtra("lines");
                string tag = lines != null ? Utils.NAME_LCHOOSER : Utils.NAME_LCREATOR;

                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag(tag);
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                if (lines != null) new LineChooserDialog(travelId, lines).Show(trans, tag); // We know the possible lines
                else new LineCreatorDialog(travelId).Show(trans, tag); // We don't know the possible lines
            }
        }

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
                int linesSize = (int) stopsLines["linesSize"];
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

    class LineChooserDialog : DialogFragment
    {
        private int travel;
        private int[] lines;

        public LineChooserDialog(int travelId, int[] lines)
        {
            this.lines = lines;
            travel = travelId;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            Realm realm = Realm.GetInstance(Utils.NAME_PREF);

            Travel t = realm.All<Travel>().Where(tr => tr.id == travel).First();
            List<Line> realmLines = new List<Line>();
            foreach (int l in lines)
            {
                var results = from li in realm.All<Line>() where li.id == l select li;
                realmLines.Add(results.First());
            }

            Dialog dialog = null;
            var builder = new AlertDialog.Builder(Activity);
            builder.SetView(Resource.Layout.LineChooser);
            builder.SetMessage("Línea tomada");
            builder.SetPositiveButton("Aceptar", (o, e) =>
            {
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
                }
            });

            dialog = builder.Create();

            LinearLayout layout = dialog.Window.DecorView.RootView as LinearLayout;
            layout.Orientation = Orientation.Vertical;
            RadioGroup group = layout.FindViewById<RadioGroup>(Resource.Id.radioGroup1);

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

    class LineCreatorDialog : DialogFragment
    {
        private int travel;

        public LineCreatorDialog(int travelId)
        {
            travel = travelId;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            Realm realm = Realm.GetInstance(Utils.NAME_PREF);

            Travel t = realm.All<Travel>().Where(tr => tr.id == travel).First();

            Dialog dialog = null;
            var builder = new AlertDialog.Builder(Activity);
            builder.SetView(Resource.Layout.LineChooser);
            builder.SetMessage("Línea tomada");
            builder.SetPositiveButton("Aceptar", (o, e) =>
            {
                EditText number = dialog.FindViewById<EditText>(Resource.Id.editText1), name = dialog.FindViewById<EditText>(Resource.Id.editText2);

                if (name.Text.Length != 0)
                {
                    int lineNumber = int.Parse(number.Text);

                    if (realm.All<Line>().Where(l => l.id == lineNumber).Count() > 0)
                    {
                        Toast.MakeText(Activity, "Error: La línea ya existe", ToastLength.Long).Show();
                        return;
                    }

                    realm.Write(() =>
                    {
                        Line line = realm.CreateObject<Line>();
                        if (lineNumber != 0) line.id = lineNumber;
                        t.line = line;

                        if ((t.bus.line != null && t.bus.line.id != t.line.id) || (t.bus.line == null))
                        {
                            t.bus.line = t.line;
                            t.bus.lastRefresh = DateTimeOffset.Now;
                        }

                        if (t.init != null)
                        {
                            t.init.lines.Add(line);
                            line.stops.Add(t.init);
                        }

                        if (t.end != null)
                        {
                            t.end.lines.Add(line);
                            line.stops.Add(t.end);
                        }
                    });
                }
            });

            dialog = builder.Create();

            return dialog;
        }
    }
}

