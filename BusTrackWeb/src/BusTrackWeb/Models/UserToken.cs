using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class UserToken
    {
        [Key]
        public string id { get; set; }
        public string sub { get; set; }
        public DateTime iat { get; set; } = DateTime.UtcNow;
        public DateTime exp { get; set; } = DateTime.UtcNow.AddYears(1);
        public long userId { get; set; }

        [ForeignKey("userId")]
        public User User { get; set; }
    }
}
