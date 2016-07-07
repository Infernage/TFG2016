using Realms;
using System;

namespace BusTrack.Data
{
    public class Travel : RealmObject
    {
        [ObjectId]
        public int id { get; set; }
        public float distance { get; set; }
        public int time { get; set; }
        public DateTimeOffset date { get; set; }
        public Line line { get; set; }
        public Bus bus { get; set; }
        public Stop init { get; set; }
        public Stop end { get; set; }
    }
}