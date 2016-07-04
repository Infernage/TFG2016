using Realms;

namespace BusTrack.Data
{
    public class Network : RealmObject
    {
        [ObjectId]
        public int id { get; set; }
        public string ssid { get; set; }
        [Indexed]
        public string mac { get; set; }
        public int numTimes { get; set; }
        public User owner { get; set; }
    }
}