using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusTrackWeb.Models
{
    public class LineHasStop
    {
        public long line_id { get; set; }
        public long stop_id { get; set; }

        [ForeignKey("line_id")]
        [JsonIgnore]
        public Line Line { get; set; }

        [ForeignKey("stop_id")]
        [JsonIgnore]
        public Stop Stop { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is LineHasStop)
            {
                LineHasStop ls = obj as LineHasStop;
                return ls.line_id == line_id && ls.stop_id == stop_id;
            }
            return base.Equals(obj);
        }
    }
}