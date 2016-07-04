using Realms;

namespace BusTrack.Data
{
    public class User : RealmObject
    {
        [ObjectId]
        public int id { get; set; }
        [Indexed]
        public string name { get; set; }
        public string hash { get; set; }
        public int bdSize { get; set; }
        public RealmList<Network> networks { get; }
        public RealmList<BusLine> travels { get; }
    }
}