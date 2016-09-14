using Newtonsoft.Json;
using Realms;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace BusTrack.Data
{
    public class Line : RealmObject, INotifyPropertyChanged
    {
        public Line()
        {
            synced = false;
            PropertyChanged += SetUnsynced;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetUnsynced(object sender, PropertyChangedEventArgs args)
        {
            if (synced && !args.PropertyName.Equals("synced")) synced = false;
        }

        [PrimaryKey]
        public long id { get; set; }

        [Indexed]
        public string name { get; set; }

        [Ignored]
        [JsonProperty("stops")]
        public List<long> jstops { get; set; }

        [JsonIgnore]
        public bool synced { get; set; }

        [JsonIgnore]
        public RealmList<Stop> stops { get; }

        public void GenerateID(Realm realm)
        {
            var query = realm.All<Line>().OrderByDescending(t => t.id);
            id = query.Any() ? query.First().id + 1 : 1;
        }
    }
}