using Android.Gms.Maps.Model;
using Android.Graphics;
using BusTrack.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BusTrack.Utilities
{
    /// <summary>
    /// Class used to perform operations with google web api.
    /// </summary>
    public class GoogleUtils
    {
        private static readonly string G_API =
            "https://maps.googleapis.com/maps/api/<apiname>/json?key=AIzaSyDVZGmOKBOdXIClT1ArDYuK3b3cGHZ6LJA&<origin>&<destination>&mode=transit&transit_mode=bus";

        private static readonly string API_DM = "distancematrix";
        private static readonly string API_DM_O = "origins=";
        private static readonly string API_DM_D = "destinations=";
        private static readonly string API_DIR = "directions";
        private static readonly string API_DIR_O = "origin=";
        private static readonly string API_DIR_D = "destination=";

        /// <summary>
        /// Gets the distance between 2 stops.
        /// </summary>
        /// <param name="init">The initial stop.</param>
        /// <param name="end">The final stop.</param>
        /// <returns>The distance in meters.</returns>
        public static long GetDistance(Stop init, Stop end)
        {
            string apiUrl = G_API.Replace("<apiname>", API_DM).Replace("<origin>", API_DM_O + init.locationString).Replace("<destination>", API_DM_D + end.locationString);
            WebClient client = new WebClient();
            string str = client.DownloadString(apiUrl);
            client.Dispose();
            var json = JObject.Parse(str);

            if (!json["status"].ToString().Equals("OK")) return 0;

            var parsed = json["rows"][0]["elements"][0];
            long distance = 0;
            if (parsed["distance"].ToString().Length > 0)
            {
                distance = parsed["distance"]["value"].ToObject<long>();
            }
            return distance;
        }

        /// <summary>
        /// Gets a route inside a travel.
        /// </summary>
        /// <param name="travel">The travel to get a route</param>
        /// <returns>A PolylineOptions with the route.</returns>
        public static PolylineOptions GetRoute(Travel travel)
        {
            string apiUrl = G_API.Replace("<apiname>", API_DIR).Replace("<origin>", API_DIR_O + travel.init.locationString).Replace("<destination>", API_DIR_D + travel.end.locationString)
                + "&departure_time=" + Utils.ToUnixEpochDate(DateTime.SpecifyKind(travel.date.DateTime, DateTimeKind.Local));
            WebClient client = new WebClient();
            string jsonStr = client.DownloadString(apiUrl);
            client.Dispose();
            var json = JObject.Parse(jsonStr);
            var routes = json["routes"];

            if (routes.Count() == 0) return new PolylineOptions();

            string encoded = routes[0]["overview_polyline"]["points"].ToString();

            List<LatLng> poly = new List<LatLng>();
            int index = 0, len = encoded.Length;
            int lat = 0, lng = 0;

            // Decodes the encoded string
            // See here: https://developers.google.com/maps/documentation/utilities/polylinealgorithm
            while (index < len)
            {
                int b, shift = 0, result = 0;
                do
                {
                    b = encoded[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);
                int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
                lat += dlat;

                shift = 0;
                result = 0;
                do
                {
                    b = encoded[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);
                int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
                lng += dlng;

                LatLng p = new LatLng(lat / 1E5, lng / 1E5);
                poly.Add(p);
            }

            return new PolylineOptions().AddAll(new Java.Util.ArrayList(poly.ToArray())).InvokeWidth(12).InvokeColor(Color.ParseColor("#05b1fb")).Geodesic(true);
        }
    }
}