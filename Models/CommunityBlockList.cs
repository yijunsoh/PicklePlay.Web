using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    // CommunityBlockList Table (Blocklist for Users within a Community)
    [Table("CommunityBlockList")]
    [Index(nameof(CommunityId), nameof(UserId), IsUnique = true)] // Prevent double blocking
    public class CommunityBlockList
    {
        [Key]
        [Column("block_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BlockId { get; set; } // Unique block record

        [Column("community_id")]
        public int CommunityId { get; set; } // Blocked community ID (FK → Community.community_id)

        [Column("user_id")]
        public int UserId { get; set; } // Blocked user ID (FK → User.user_id)

        [Column("blockByAdminId")]
        public int BlockByAdminId { get; set; } // Admin who blocked (FK → User.user_id)

        [Column("block_reason", TypeName = "text")]
        public string? BlockReason { get; set; } // Reason for blocking

        [Column("block_date")]
        public DateTime BlockDate { get; set; } = DateTime.UtcNow; // Date of block

        // Navigation Properties

        [ForeignKey("CommunityId")]
        public virtual Community Community { get; set; } = null!;

        [ForeignKey("UserId")]
        public virtual User BlockedUser { get; set; } = null!; // The user who is blocked

        [ForeignKey("BlockByAdminId")]
        public virtual User BlockingAdmin { get; set; } = null!; // The admin (or community admin) who performed the block
    }
}