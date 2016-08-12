using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BusTrackWeb.Models
{
    public class User
    {
        public User()
        {
            Travels = new HashSet<Travel>();
        }
        [Key]
        public long id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string hash { get; set; }

        [InverseProperty("User")]
        public ICollection<Travel> Travels { get; set; }
    }
}
