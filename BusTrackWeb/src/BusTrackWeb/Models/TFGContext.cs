using Microsoft.EntityFrameworkCore;

namespace BusTrackWeb.Models
{
    public class TFGContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // idMM
            modelBuilder.Entity<LineHasStop>().HasKey(k => new { k.line_id, k.stop_id });
            modelBuilder.Entity<LineHasStop>().HasOne(lhs => lhs.Line).WithMany(l => l.LineStops).HasForeignKey(lhs => lhs.line_id);
            modelBuilder.Entity<LineHasStop>().HasOne(lhs => lhs.Stop).WithMany(s => s.LineStops).HasForeignKey(lhs => lhs.stop_id);

            // idTravel
            modelBuilder.Entity<Travel>().HasKey(t => new { t.id, t.lineId, t.initId, t.busId });
            modelBuilder.Entity<Travel>().Property(t => t.id).ValueGeneratedOnAdd();

            // idUserToken
            modelBuilder.Entity<UserToken>().HasKey(t => new { t.id, t.userId });
            modelBuilder.Entity<UserToken>().Property(t => t.id).ValueGeneratedOnAdd();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(@"Host=localhost;Database=TFG;Username=postgres;Password=root");
        }

        public DbSet<LineHasStop> LineHasStop { get; set; }
        public DbSet<Line> Line { get; set; }
        public DbSet<User> User { get; set; }
        public DbSet<Stop> Stop { get; set; }
        public DbSet<Bus> Bus { get; set; }
        public DbSet<Travel> Travel { get; set; }
    }
}