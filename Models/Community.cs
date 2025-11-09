using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    // Community Table
    [Table("Community")]
    [Index(nameof(CommunityName), IsUnique = true)]
    public class Community
    {
        [Key]
        [Column("community_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CommunityId { get; set; }

        [Required]
        [MaxLength(150)]
        [Column("community_name")]
        public string CommunityName { get; set; } = null!; // Not Null, Unique

        [Column("description", TypeName = "text")]
        public string? Description { get; set; }

        [Column("createByUserId")]
        public int CreateByUserId { get; set; } // FK → User.user_id (The original requester/owner)

        [MaxLength(150)]
        [Column("community_location")]
        public string? CommunityLocation { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(50)]
        [Column("community_type")]
        public string CommunityType { get; set; } = null!; // Not Null (e.g., Public/Private)

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Active"; // Active / Inactive / Suspended

        [Column("lastActivityDate")]
        public DateTime? LastActivityDate { get; set; }

        [MaxLength(255)]
        [Column("community_pic")]
        public string? CommunityPic { get; set; }

        // Navigation Properties
        [ForeignKey("CreateByUserId")]
        public virtual User Creator { get; set; } = null!;

        [Column("deletion_reason")]
        [StringLength(500)]
        public string? DeletionReason { get; set; }

        [Column("deleted_by_user_id")]
        public int? DeletedByUserId { get; set; }

        [Column("deletion_date")]
        public DateTime? DeletionDate { get; set; }

        // Navigation property
        [ForeignKey("DeletedByUserId")]
        public virtual User? DeletedByUser { get; set; }

        // Add these properties to your Community class in Community.cs
        [NotMapped] // This property won't be stored in the database
        public IFormFile? ProfileImageFile { get; set; }

        [NotMapped]
        public string? TempImageUrl { get; set; } // For preview purposes

        public virtual ICollection<CommunityMember> Memberships { get; set; } = new List<CommunityMember>();
        public virtual ICollection<CommunityBlockList> BlockedUsers { get; set; } = new List<CommunityBlockList>();
        public virtual ICollection<CommunityAnnouncement> Announcements { get; set; } = new List<CommunityAnnouncement>();
    }
}