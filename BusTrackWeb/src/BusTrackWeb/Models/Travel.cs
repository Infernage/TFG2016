using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusTrackWeb.Models
{
    public class Travel
    {
        [Key]
        [JsonIgnore]
        public long id { get; set; }

        public long distance { get; set; }
        public int time { get; set; }
        public DateTime date { get; set; }
        public long lineId { get; set; }
        public long initId { get; set; }
        public long? endId { get; set; }
        public long? userId { get; set; }
        public string busId { get; set; }

        [ForeignKey("lineId")]
        [JsonIgnore]
        public Line Line { get; set; }

        [ForeignKey("initId")]
        [JsonIgnore]
        public Stop Init { get; set; }

        [ForeignKey("endId")]
        [JsonIgnore]
        public Stop End { get; set; }

        [ForeignKey("userId")]
        [JsonIgnore]
        public User User { get; set; }

        [ForeignKey("busId")]
        [JsonIgnore]
        public Bus Bus { get; set; }
    }
}