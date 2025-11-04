using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    // CommunityRequest Table
    [Table("CommunityRequest")]
    public class CommunityRequest
    {
        [Key]
        [Column("request_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int RequestId { get; set; }

        [Column("requestByUserId")]
        public int RequestByUserId { get; set; } // FK → User.user_id

        [Required]
        [MaxLength(150)]
        [Column("communityName")]
        public string CommunityName { get; set; } = null!;

        [Column("description", TypeName = "text")]
        public string? Description { get; set; }

        [MaxLength(150)]
        [Column("community_location")]
        public string? CommunityLocation { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("community_type")]
        public string CommunityType { get; set; } = "Public"; // Not Null, Public / Private

        [Column("request_date")]
        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(20)]
        [Column("request_status")]
        public string RequestStatus { get; set; } = "Pending"; // Pending / Approved / Rejected

        // Navigation Property
        [ForeignKey("RequestByUserId")]
        public virtual User RequestByUser { get; set; } = null!;
    }
}