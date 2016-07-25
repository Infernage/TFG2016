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
using BusTrack.Data;
using System.Net;
using Newtonsoft.Json.Linq;

namespace BusTrack.Utilities
{
    class Utils
    {
        public static readonly string PREF_DATA_LIMIT = "limitData";
        public static readonly string PREF_USER_ID = "userID";
        public static readonly string PREF_NETWORKS = "networks";
        public static readonly string NAME_PREF = "BusTrack";

        public static readonly string NAME_LCHOOSER = "LineChooser";
        public static readonly string NAME_LCREATOR = "LineCreator";

        public static readonly float POLLUTION_CAR = 119F, POLLUTION_BUS = 104F, POLLUTION_BUS_E = 18.6F;

        private static readonly string DISTANCE_MATRIX = 
            "https://maps.googleapis.com/maps/api/distancematrix/json?key=AIzaSyDVZGmOKBOdXIClT1ArDYuK3b3cGHZ6LJA&origins=<->origin<->&destinations=<->destination<->&mode=transit&transit_mode=bus";

        public static long GetDistance(Stop init, Stop end)
        {
            string apiUrl = DISTANCE_MATRIX.Replace("<->origin<->", init.locationString).Replace("<->destination<->", end.locationString);
            WebClient client = new WebClient();
            string json = client.DownloadString(apiUrl);
            client.Dispose();
            var parsed = JObject.Parse(json)["rows"].Children().First()["elements"].Children().First();
            long distance = 0;
            if (parsed.Contains("distance")){
                distance = parsed["distance"]["value"].Value<long>();
            }
            return distance;
        }
    }

    /// <summary>
    /// Class used to load the application service Scanner when Android boots up
    /// </summary>
    [BroadcastReceiver]
    [IntentFilter(new[] { Intent.ActionBootCompleted }, Categories = new[] { Intent.CategoryDefault })]
    class BootLoader : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent.Action != null && intent.Action == Intent.ActionBootCompleted)
            {
                // Start service
                Intent service = new Intent(context, typeof(Scanner));
                service.AddFlags(ActivityFlags.NewTask);
                service.AddFlags(ActivityFlags.FromBackground);
                context.ApplicationContext.StartService(service);
            }
        }
    }
}