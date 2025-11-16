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
        public DbSet<Community> Communities { get; set; }
        public DbSet<CommunityRequest> CommunityRequests { get; set; }
        public DbSet<CommunityMember> CommunityMembers { get; set; }
        public DbSet<CommunityBlockList> CommunityBlockLists { get; set; }
        public DbSet<CommunityAnnouncement> CommunityAnnouncements { get; set; } = null!;
        public DbSet<CommunityInvitation> CommunityInvitations { get; set; }


        public DbSet<Schedule> Schedules { get; set; }
        public DbSet<Competition> Competitions { get; set; }



        public DbSet<ScheduleParticipant> ScheduleParticipants { get; set; }
        public virtual DbSet<Bookmark> Bookmarks { get; set; }
        public virtual DbSet<Team> Teams { get; set; }
        public virtual DbSet<TeamMember> TeamMembers { get; set; }

        public virtual DbSet<TeamInvitation> TeamInvitations { get; set; }
        public virtual DbSet<Friendship> Friendships { get; set; }
        public virtual DbSet<Pool> Pools { get; set; }
        public DbSet<Match> Matches { get; set; }
        // --- ADD THIS NEW DbSet ---
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Endorsement> Endorsements { get; set; }
        public DbSet<Award> Awards { get; set; }

        public DbSet<Message> Messages { get; set; }
        public DbSet<CommunityChatMessage> CommunityChatMessages { get; set; }



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
            // --- TeamMember relationships ---
            modelBuilder.Entity<TeamMember>()
                .HasOne(tm => tm.Team)
                .WithMany(t => t.TeamMembers)
                .HasForeignKey(tm => tm.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TeamMember>()
                .HasOne(tm => tm.User)
                .WithMany() // Assuming User doesn't have a direct ICollection<TeamMember>
                .HasForeignKey(tm => tm.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Friendship relationships (This fixes the warning) ---
            modelBuilder.Entity<Friendship>()
                .HasOne(f => f.UserOne)
                .WithMany(u => u.FriendshipsSent) // Maps to User.FriendshipsSent
                .HasForeignKey(f => f.UserOneId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Friendship>()
                .HasOne(f => f.UserTwo)
                .WithMany(u => u.FriendshipsReceived) // Maps to User.FriendshipsReceived
                .HasForeignKey(f => f.UserTwoId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- TeamInvitation relationships (This fixes the warning) ---
            modelBuilder.Entity<TeamInvitation>()
                .HasOne(ti => ti.Inviter)
                .WithMany(u => u.SentTeamInvitations) // Maps to User.SentTeamInvitations
                .HasForeignKey(ti => ti.InviterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamInvitation>()
                .HasOne(ti => ti.Invitee)
                .WithMany(u => u.ReceivedTeamInvitations) // Maps to User.ReceivedTeamInvitations
                .HasForeignKey(ti => ti.InviteeUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // This maps the invitation back to the team
            modelBuilder.Entity<TeamInvitation>()
                .HasOne(ti => ti.Team)
                .WithMany(t => t.Invitations) // Maps to Team.Invitations
                .HasForeignKey(ti => ti.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            // --- *** NEWLY ADDED RULES FOR POOLS *** ---
            modelBuilder.Entity<Team>()
                .HasOne(t => t.Pool)
                .WithMany(p => p.Teams)
                .HasForeignKey(t => t.PoolId)
                .OnDelete(DeleteBehavior.SetNull);
            // --- ADD THIS BLOCK ---
            // Configure one-way navigation for Match -> Team (Winner)
            modelBuilder.Entity<Match>()
                .HasOne(m => m.Winner)
                .WithMany() // No navigation property on Team
                .HasForeignKey(m => m.WinnerId)
                .OnDelete(DeleteBehavior.Restrict); // Avoid cascade delete issues

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Team1)
                .WithMany()
                .HasForeignKey(m => m.Team1Id)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Team2)
                .WithMany()
                .HasForeignKey(m => m.Team2Id)
                .OnDelete(DeleteBehavior.Restrict);
            // --- END OF BLOCK ---

            modelBuilder.Entity<Endorsement>()
        .HasOne(e => e.GiverUser)
        .WithMany() // No navigation property on User
        .HasForeignKey(e => e.GiverUserId)
        .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Endorsement>()
                .HasOne(e => e.ReceiverUser)
                .WithMany() // No navigation property on User
                .HasForeignKey(e => e.ReceiverUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Message configuration
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.Receiver)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            // Notification configuration
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.RelatedUser)
                .WithMany()
                .HasForeignKey(n => n.RelatedUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }


    }

}