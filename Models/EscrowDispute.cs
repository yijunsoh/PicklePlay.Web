using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("Escrow_Dispute")]
    public class EscrowDispute
    {
        [Key]
        [Column("dispute_id")]
        public int DisputeId { get; set; }

        [Required]
        [Column("schedule_id")]
        public int ScheduleId { get; set; }

        [Required]
        [Column("raisedByUserId")]
        public int RaisedByUserId { get; set; }

        [Required]
        [Column("dispute_reason", TypeName = "text")]
        public string DisputeReason { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        [Column("admin_decision")]
        public string AdminDecision { get; set; } = "Pending";

        [Column("decision_date")]
        public DateTime? DecisionDate { get; set; }

        [Required]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // NEW COLUMN (remove refund_process)
        [Column("admin_review_note", TypeName = "text")]
        public string? AdminReviewNote { get; set; }

        // Navigation
        [ForeignKey("ScheduleId")]
        public virtual Schedule Schedule { get; set; } = null!;

        [ForeignKey("RaisedByUserId")]
        public virtual User RaisedByUser { get; set; } = null!;
    }


}