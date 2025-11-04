using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    [Table("TeamMember")]
    public class TeamMember
    {
        [Key]
        public int TeamMemberId { get; set; }

        [Required]
        public int TeamId { get; set; }
        [ForeignKey("TeamId")]
        public virtual required Team Team { get; set; }

        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual required User User { get; set; }

        [Required]
        public TeamMemberStatus Status { get; set; } = TeamMemberStatus.Joined;
    }

    public enum TeamMemberStatus
    {
        InvitationPending,
        Joined
    }
}