using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        [NotMapped]
        public ICollection<long> stops // Used when is serialized
        {
            get
            {
                List<long> res = new List<long>();
                foreach (LineHasStop ls in LineStops) res.Add(ls.stop_id);
                return res;
            }
        }

        [InverseProperty("Line")]
        [JsonIgnore]
        public ICollection<Travel> Travels { get; set; }

        [InverseProperty("Line")]
        [JsonIgnore]
        public ICollection<LineHasStop> LineStops { get; set; }

        [InverseProperty("Line")]
        [JsonIgnore]
        public ICollection<Bus> Buses { get; set; }
    }
}