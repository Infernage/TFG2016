using Realms;
using System;

namespace BusTrack.Data
{
    public class Travel : RealmObject
    {
        [ObjectId]
        public int id { get; set; }
        public long distance { get; set; }
        public int time { get; set; }
        public DateTimeOffset date { get; set; }
        public int userId { get; set; }
        public Line line { get; set; }
        public Bus bus { get; set; }
        public Stop init { get; set; }
        public Stop end { get; set; }
    }
}