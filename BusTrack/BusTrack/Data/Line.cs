using Newtonsoft.Json;
using Realms;
using System.Collections.Generic;

namespace BusTrack.Data
{
    public class Line : RealmObject
    {
        [JsonProperty("id")]
        [ObjectId]
        public int id { get; set; }

        [JsonProperty("name")]
        [Indexed]
        public string name { get; set; }

        public RealmList<Bus> buses { get; }

        [JsonProperty("realmStops")]
        public RealmList<Stop> stops { get; }

        [JsonProperty("stops")]
        [Ignored]
        public List<long> stopIds { get; set; }
    }
}