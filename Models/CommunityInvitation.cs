using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("CommunityInvitation")]
    [Index(nameof(CommunityId), nameof(InviteeUserId), nameof(Status), IsUnique = true)]
    public class CommunityInvitation
    {
        [Key]
        [Column("invitation_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int InvitationId { get; set; }

        [Column("community_id")]
        public int CommunityId { get; set; } // FK → Community.community_id

        [Column("invitee_user_id")]
        public int InviteeUserId { get; set; } // FK → User.user_id (receiver)

        [Column("inviter_user_id")]
        public int InviterUserId { get; set; } // FK → User.user_id (sender)

        [Required]
        [MaxLength(20)]
        [Column("role")]
        public string Role { get; set; } = "Member"; // Member / Moderator / Admin

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Pending"; // Pending / Accepted / Declined

        [Required]
        [Column("date_sent")]
        public DateTime DateSent { get; set; }  // set manually via DateTime.Now in code

        [Column("date_responded")]
        public DateTime? DateResponded { get; set; }

        // Navigation properties
        [ForeignKey(nameof(CommunityId))]
        public virtual Community Community { get; set; } = null!;

        [ForeignKey(nameof(InviteeUserId))]
        public virtual User Invitee { get; set; } = null!;

        [ForeignKey(nameof(InviterUserId))]
        public virtual User Inviter { get; set; } = null!;
    }
}
