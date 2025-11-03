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
        }
        // Add other DbSets for your 25+ tables later
    }
    
}