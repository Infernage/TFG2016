using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class Bus
    {
        public Bus() { Travels = new HashSet<Travel>(); }
        [Key]
        public string mac { get; set; }
        public DateTime lastRefresh { get; set; }
        public long lineId { get; set; }

        [ForeignKey("lineId")]
        public Line Line { get; set; }
        [InverseProperty("Bus")]
        public ICollection<Travel> Travels { get; set; }
    }
}
