using Android.Locations;
using Newtonsoft.Json;
using Realms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace BusTrack.Data
{
    public class Stop : RealmObject, INotifyPropertyChanged
    {
        public Stop()
        {
            synced = false;
            PropertyChanged += SetUnsynced;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetUnsynced(object sender, PropertyChangedEventArgs args)
        {
            if (synced && !args.PropertyName.Equals("synced")) synced = false;
        }

        [PrimaryKey]
        public long id { get; set; }

        private double latitude { get; set; }
        private double longitude { get; set; }

        [JsonProperty("lines")]
        [Ignored]
        public List<long> jlines { get; set; }

        [JsonIgnore]
        public bool synced { get; set; }

        [JsonIgnore]
        [Ignored]
        public Location location
        {
            get
            {
                Location value = new Location("");
                value.Latitude = latitude;
                value.Longitude = longitude;
                return value;
            }
            set
            {
                if (value == null) throw new Exception("Location database added is null!");
                latitude = value.Latitude;
                longitude = value.Longitude;
            }
        }

        [JsonIgnore]
        [Ignored]
        public string locationString
        {
            get
            {
                return location.Latitude.ToString(CultureInfo.GetCultureInfo("en-US")) + ',' + location.Longitude.ToString(CultureInfo.GetCultureInfo("en-US"));
            }
        }

        [JsonIgnore]
        public RealmList<Line> lines { get; }

        public void GenerateID(Realm realm)
        {
            var query = realm.All<Stop>().OrderByDescending(t => t.id);
            id = query.Any() ? query.First().id + 1 : 1;
        }
    }
}