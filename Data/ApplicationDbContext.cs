using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;

namespace PicklePlay.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<Escrow> Escrows { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<EscrowDispute> EscrowDisputes { get; set; }
        public DbSet<Schedule> Schedules { get; set; }
        public DbSet<Competition> Competitions { get; set; }
        
        public DbSet<ScheduleParticipant> ScheduleParticipants { get; set; }
        public virtual DbSet<Bookmark> Bookmarks { get; set; }
        public virtual DbSet<Team> Teams { get; set; }
    public virtual DbSet<TeamMember> TeamMembers { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- CONFIGURE ONE-TO-ONE RELATIONSHIP ---
            modelBuilder.Entity<Schedule>()
                .HasOne(s => s.Competition) // Schedule has one Competition (or null)
                .WithOne(c => c.Schedule)   // Competition has one required Schedule
                .HasForeignKey<Competition>(c => c.ScheduleId); // The FK is in Competition table
            // --- END CONFIGURATION ---

            // --- Configure Enums for Competition (if needed) ---
            modelBuilder.Entity<Competition>()
                .Property(c => c.Format)
                .HasConversion<byte>(); // Convert TINYINT to byte enum

            modelBuilder.Entity<Competition>()
                .Property(c => c.StandingCalculation)
                .HasConversion<byte>();

            // --- Existing Enum Conversions for Schedule (keep these) ---
            // modelBuilder.Entity<Schedule>()...

            // Add this to prevent issues when a Team and TeamMember reference each other
        modelBuilder.Entity<TeamMember>()
            .HasOne(tm => tm.Team)
            .WithMany(t => t.TeamMembers)
            .HasForeignKey(tm => tm.TeamId)
            .OnDelete(DeleteBehavior.Cascade); // or DeleteBehavior.Restrict

        modelBuilder.Entity<TeamMember>()
            .HasOne(tm => tm.User)
            .WithMany() // Assuming User doesn't have a direct ICollection<TeamMember>
            .HasForeignKey(tm => tm.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        }
        // Add other DbSets for your 25+ tables later
    }
    
}