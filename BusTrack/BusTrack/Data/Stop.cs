using Android.Locations;
using Newtonsoft.Json;
using Realms;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BusTrack.Data
{
    public class Stop : RealmObject
    {
        [JsonProperty("id")]
        [ObjectId]
        public long id { get; set; }

        [Ignored] // Just in case
        public Location location
        {
            get
            {
                Location value = new Location("");
                value.Latitude = lat;
                value.Longitude = lon;
                return value;
            }
            set
            {
                if (value == null) throw new Exception("Location database added is null!");
                lat = value.Latitude;
                lon = value.Longitude;
            }
        }

        [Ignored]
        public string locationString
        {
            get
            {
                return location.Latitude.ToString(CultureInfo.GetCultureInfo("en-US")) + ',' + location.Longitude.ToString(CultureInfo.GetCultureInfo("en-US"));
            }
        }

        [JsonProperty("realmLines")]
        public RealmList<Line> lines { get; }

        public RealmList<Travel> initTravels { get; }
        public RealmList<Travel> endingTravels { get; }

        private double lat { get; set; }
        private double lon { get; set; }

        /// <summary>
        /// Ignored in realm
        /// </summary>
        [JsonProperty("position")]
        [Ignored]
        public string position { get; set; }

        /// <summary>
        /// Ignored in realm
        /// </summary>
        [JsonProperty("lines")]
        [Ignored]
        public List<int> lineIds { get; set; }

        public void GenerateID(Realm realm)
        {
            var query = realm.All<Stop>().OrderByDescending(s => s.id);
            id = query.Any() ? query.First().id + 1 : 1;
        }
    }
}