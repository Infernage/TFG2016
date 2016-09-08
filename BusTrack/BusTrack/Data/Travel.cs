using Realms;
using System;
using System.Linq;

namespace BusTrack.Data
{
    public class Travel : RealmObject
    {
        [ObjectId]
        public int id { get; set; }

        public long distance { get; set; }
        public int time { get; set; }
        public DateTimeOffset date { get; set; }
        public long userId { get; set; }
        public Line line { get; set; }
        public Bus bus { get; set; }
        public Stop init { get; set; }
        public Stop end { get; set; }

        public void GenerateID(Realm realm)
        {
            var query = realm.All<Travel>().OrderByDescending(t => t.id);
            id = query.Any() ? query.First().id + 1 : 1;
        }
    }
}