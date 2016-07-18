using Newtonsoft.Json;
using Realms;
using System;
using System.Collections.Generic;

namespace BusTrack.Data
{
    public class Stop : RealmObject
    {
        [JsonProperty("id")]
        [ObjectId]
        public int id { get; set; }

        public Tuple<float, float> location
        {
            get
            {
                return new Tuple<float, float>(lat, lon);
            }
            set
            {
                if (value == null) throw new Exception("Location database added is null!");
                lat = value.Item1;
                lon = value.Item2;
            }
        }
        public RealmList<Line> lines { get; }

        private float lat { get; set; }
        private float lon { get; set; }
        [JsonProperty("position")]
        [Ignored]
        public string position { get; set; }
        [JsonProperty("lines")]
        [Ignored]
        public List<int> lineIds { get; set; }
    }
}