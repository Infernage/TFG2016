using Realms;

namespace BusTrack.Data
{
    public class Bus : RealmObject
    {
        [ObjectId]
        public string mac { get; set; }
        public Line line { get; set; }
        public RealmList<Travel> travels { get; }
    }
}