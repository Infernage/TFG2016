using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class Travel
    {
        [Key]
        public long id { get; set; }
        public long distance { get; set; }
        public int time { get; set; }
        public DateTime date { get; set; }
        public long lineId { get; set; }
        public long initId { get; set; }
        public long endId { get; set; }
        public long? userId { get; set; }
        public string busId { get; set; }

        [ForeignKey("lineId")]
        public Line Line { get; set; }
        [ForeignKey("initId")]
        public Stop Init { get; set; }
        [ForeignKey("endId")]
        public Stop End { get; set; }
        [ForeignKey("userId")]
        public User User { get; set; }
        [ForeignKey("busId")]
        public Bus Bus { get; set; }
    }
}
