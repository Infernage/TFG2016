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
                            newStop.location = new Tuple<float, float>(float.Parse(location[0]), float.Parse(location[1]));
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

                            // Get all stop with the ID stored in JSON array
                            var lineStops = from stop in realm.All<Stop>()
                                            where line.stopIds.Contains(stop.id)
                                            select stop;
                            foreach (Stop s in lineStops)
                            {
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
}

