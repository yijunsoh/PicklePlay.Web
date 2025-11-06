using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    [Table("Team")]
    public class Team
    {
        [Key]
        public int TeamId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string TeamName { get; set; }

        [MaxLength(512)]
        public string? TeamIconUrl { get; set; }

        [Required]
        public TeamStatus Status { get; set; } = TeamStatus.Pending;

        // Foreign key to the competition (which is a Schedule)
        [Required]
        public int ScheduleId { get; set; }
        [ForeignKey("ScheduleId")]
        public virtual required Schedule Schedule { get; set; }

        // Foreign key to the user who created the team (Captain)
        [Required]
        public int CreatedByUserId { get; set; }
        [ForeignKey("CreatedByUserId")]
        public virtual required User Captain { get; set; }

        // Navigation property for all members in this team
        public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();

        // --- ADD THIS NEW NAVIGATION PROPERTY ---
        public virtual ICollection<TeamInvitation> Invitations { get; set; } = new List<TeamInvitation>();
        // --- *** ADD THESE NEW PROPERTIES *** ---

        // For Pool Play: Stores which pool this team is in
        public int? PoolId { get; set; }
        [ForeignKey("PoolId")]
        public virtual Pool? Pool { get; set; }

        // For Elimination: Stores the seed (1-16)
        public int? BracketSeed { get; set; }
    }

    public enum TeamStatus
    {
        Pending,
        Confirmed,
        Cancelled
    }

    
}