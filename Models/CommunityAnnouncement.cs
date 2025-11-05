using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;

namespace PicklePlay.Models
{
    [Table("CommunityAnnouncement")]
    public class CommunityAnnouncement
    {
        [Key]
        [Column("announcement_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AnnouncementId { get; set; }

        [Column("community_id")]
        public int CommunityId { get; set; } // FK to Community.community_id

        [Column("poster_user_id")]
        public int PosterUserId { get; set; } // FK to User.user_id (The admin who posted it)

        [Required]
        [MaxLength(255)]
        [Column("title")]
        public string Title { get; set; } = null!;

        [Required]
        [Column("content", TypeName = "text")]
        public string Content { get; set; } = null!;

        [Column("post_date")]
        public DateTime PostDate { get; set; } = DateTime.UtcNow;

        [Column("expiry_date")]
        public DateTime? ExpiryDate { get; set; } // Optional: for automatic removal

        // Navigation Properties
        [ForeignKey("CommunityId")]
        public virtual Community Community { get; set; } = null!;
        
        [ForeignKey("PosterUserId")]
        public virtual User Poster { get; set; } = null!;
    }
}