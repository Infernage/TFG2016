using Newtonsoft.Json;
using NpgsqlTypes;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        [NotMapped]
        public double latitude
        {
            get
            {
                return position.X;
            }
            set
            {
                NpgsqlPoint p = position;
                p.X = value;
                position = p;
            }
        }

        [NotMapped]
        public double longitude
        {
            get
            {
                return position.Y;
            }
            set
            {
                NpgsqlPoint p = position;
                p.Y = value;
                position = p;
            }
        }
        [NotMapped]
        public ICollection<long> lines // Used when is serialized
        {
            get
            {
                List<long> res = new List<long>();
                foreach (LineHasStop ls in LineStops) res.Add(ls.line_id);
                return res;
            }
        }

        [JsonIgnore]
        public NpgsqlPoint position { get; set; }

        [InverseProperty("Stop")]
        [JsonIgnore]
        public ICollection<LineHasStop> LineStops { get; set; }

        [InverseProperty("Init")]
        [JsonIgnore]
        public ICollection<Travel> InitialTravels { get; set; }

        [InverseProperty("End")]
        [JsonIgnore]
        public ICollection<Travel> EndingTravels { get; set; }
    }
}