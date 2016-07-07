using Realms;
using System;

namespace BusTrack.Data
{
    public class Stop : RealmObject
    {
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
    }
}