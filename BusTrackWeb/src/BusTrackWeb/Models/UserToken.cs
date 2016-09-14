using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusTrackWeb.Models
{
    public class UserToken
    {
        [Key]
        public string id { get; set; }

        public string sub { get; set; }
        public DateTime iat { get; set; } = DateTime.UtcNow;
        public DateTime exp { get; set; } = DateTime.UtcNow.AddMonths(1);
        public long userId { get; set; }

        [ForeignKey("userId")]
        public User User { get; set; }
    }
}