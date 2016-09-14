using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusTrackWeb.Models
{
    public class Bus
    {
        public Bus()
        {
            Travels = new HashSet<Travel>();
        }

        [Key]
        public string mac { get; set; }

        public DateTime lastRefresh { get; set; }
        public long? lineId { get; set; }

        [ForeignKey("lineId")]
        [JsonIgnore]
        public Line Line { get; set; }

        [InverseProperty("Bus")]
        [JsonIgnore]
        public ICollection<Travel> Travels { get; set; }
    }
}