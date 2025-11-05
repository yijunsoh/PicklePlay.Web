using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    [Table("TeamInvitation")]
    public class TeamInvitation
    {
        [Key]
        public int InvitationId { get; set; }

        [Required]
        public int TeamId { get; set; }
        [ForeignKey("TeamId")]
        public virtual Team ?Team { get; set; }

        [Required]
        public int InviterUserId { get; set; }
        [ForeignKey("InviterUserId")]
        public virtual User ?Inviter { get; set; }

        [Required]
        public int InviteeUserId { get; set; }
        [ForeignKey("InviteeUserId")]
        public virtual User ?Invitee { get; set; }

        [Required]
        public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

        [Required]
        public DateTime DateSent { get; set; } = DateTime.UtcNow;
    }

    public enum InvitationStatus
    {
        Pending,
        Accepted,
        Declined
    }
}