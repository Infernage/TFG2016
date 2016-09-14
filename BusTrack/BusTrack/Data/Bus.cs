using Newtonsoft.Json;
using Realms;
using System;
using System.ComponentModel;

namespace BusTrack.Data
{
    public class Bus : RealmObject, INotifyPropertyChanged
    {
        public Bus()
        {
            synced = false;
            PropertyChanged += SetUnsynced;
        }

        private void SetUnsynced(object sender, PropertyChangedEventArgs args)
        {
            if (synced && !args.PropertyName.Equals("synced")) synced = false;
        }

        [PrimaryKey]
        public string mac { get; set; }

        public DateTimeOffset lastRefresh { get; set; }
        internal long? lineId { get; set; }

        [JsonIgnore]
        public bool synced { get; set; }

        [JsonIgnore]
        public Line line { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}