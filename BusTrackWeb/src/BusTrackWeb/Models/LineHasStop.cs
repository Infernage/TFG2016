using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class LineHasStop
    {
        public long line_id { get; set; }
        public long stop_id { get; set; }
        public long? next { get; set; }
        public long? previous { get; set; }

        [ForeignKey("line_id")]
        public Line Line { get; set; }
        [ForeignKey("stop_id")]
        public Stop Stop { get; set; }
    }
}
