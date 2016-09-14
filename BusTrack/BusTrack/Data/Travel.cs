using Newtonsoft.Json;
using Realms;
using System;
using System.Linq;

namespace BusTrack.Data
{
    public class Travel : RealmObject
    {
        public Travel()
        {
            synced = false;
        }

        [PrimaryKey]
        [JsonIgnore]
        public long id { get; set; }

        public long distance { get; set; }
        public int time { get; set; }
        public DateTimeOffset date { get; set; }
        public long userId { get; set; }
        public long lineId { get { return line.id; } }
        public string busId { get { return bus.mac; } }
        public long initId { get { return init.id; } }
        public long endId { get { return end.id; } }

        [JsonIgnore]
        public Line line { get; set; }

        [JsonIgnore]
        public Bus bus { get; set; }

        [JsonIgnore]
        public Stop init { get; set; }

        [JsonIgnore]
        public Stop end { get; set; }

        [JsonIgnore]
        public bool synced { get; set; }

        public void GenerateID(Realm realm)
        {
            var query = realm.All<Travel>().OrderByDescending(t => t.id);
            id = query.Any() ? query.First().id + 1 : 1;
        }
    }
}