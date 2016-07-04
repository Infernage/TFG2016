using System;
using Realms;

namespace BusTrack.Data
{
    public class BusLine : RealmObject
    {
        [ObjectId]
        public int id { get; set; }
        [Indexed]
        public string name{ get; set; }
        public float distance { get; set; }
        public float time { get; set; }
        public DateTimeOffset date { get; set; }
        public float posInit { get; set; }
        public float posEnd { get; set; }
        public User owner { get; set; }
    }
}