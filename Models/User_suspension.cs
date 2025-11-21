using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("User_Suspension")]
    public class UserSuspension
    {
        [Key]
        [Column("suspension_id")]
        public int SuspensionId { get; set; }

        // User being suspended
        [Column("user_id")]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        // Reporter (the user who submitted report)
        [Column("reported_by_user_id")]
        public int ReportedByUserId { get; set; }
        public User ReportedBy { get; set; } = null!;

        [Required]
        [Column("report_reason", TypeName = "text")]
        public string ReportReason { get; set; } = null!;

        [Column("admin_decision")]
        public string AdminDecision { get; set; } = "Pending";
        // Pending / Approved / Rejected

        [Column("suspension_start")]
        public DateTime? SuspensionStart { get; set; }

        [Column("suspension_end")]
        public DateTime? SuspensionEnd { get; set; }

        // Reason why rejected by admin
        [Column("rejection_reason", TypeName = "text")]
        public string? RejectionReason { get; set; }

        [Column("is_banned")]
        public bool IsBanned { get; set; } = false;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
