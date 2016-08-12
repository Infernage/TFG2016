using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class Line
    {
        public Line()
        {
            Travels = new HashSet<Travel>();
            LineStops = new HashSet<LineHasStop>();
            Buses = new HashSet<Bus>();
        }
        [Key]
        public long id { get; set; }
        public string name { get; set; }

        [InverseProperty("Line")]
        public ICollection<Travel> Travels { get; set; }
        [InverseProperty("Line")]
        public ICollection<LineHasStop> LineStops { get; set; }
        [InverseProperty("Line")]
        public ICollection<Bus> Buses { get; set; }
    }
}
