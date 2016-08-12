using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class Stop
    {
        public Stop()
        {
            LineStops = new HashSet<LineHasStop>();
            InitialTravels = new HashSet<Travel>();
            EndingTravels = new HashSet<Travel>();
        }
        [Key]
        public long id { get; set; }
        public NpgsqlPoint position { get; set; }
        
        [InverseProperty("Stop")]
        public ICollection<LineHasStop> LineStops { get; set; }
        [InverseProperty("Init")]
        public ICollection<Travel> InitialTravels { get; set; }
        [InverseProperty("End")]
        public ICollection<Travel> EndingTravels { get; set; }
    }
}
