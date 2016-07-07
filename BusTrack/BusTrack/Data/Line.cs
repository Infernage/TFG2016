using Realms;

namespace BusTrack.Data
{
    public class Line : RealmObject
    {
        [ObjectId]
        public int id { get; set; }
        [Indexed]
        public string name{ get; set; }
        public RealmList<Bus> buses { get; }
        public RealmList<Travel> travels { get; }
        public RealmList<Stop> stops { get; }
    }
}