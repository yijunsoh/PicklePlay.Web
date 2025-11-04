using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    // CommunityMember Table (Join table for User and Community)
    [Table("CommunityMember")]
    [Index(nameof(CommunityId), nameof(UserId), IsUnique = true)] // Composite Unique Index
    public class CommunityMember
    {
        [Key]
        [Column("member_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int MemberId { get; set; }

        [Column("community_id")]
        public int CommunityId { get; set; } // FK → Community.community_id

        [Column("user_id")]
        public int UserId { get; set; } // FK → User.user_id

        [Required]
        [MaxLength(50)]
        [Column("community_role")]
        public string CommunityRole { get; set; } = "Member"; // Admin/Member/Moderator

        [Column("join_date")]
        public DateTime JoinDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Active"; // Active / Inactive

        // Navigation Properties
        [ForeignKey("CommunityId")]
        public virtual Community Community { get; set; } = null!;

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
    }
}