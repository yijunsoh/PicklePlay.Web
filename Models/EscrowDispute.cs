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
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int DisputeId { get; set; }

        [Required]
        [Column("escrow_id")]
        public int EscrowId { get; set; }

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

        [MaxLength(50)]
        [Column("refund_process")]
        public string? RefundProcess { get; set; }

        [Required]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("EscrowId")]
        public virtual Escrow Escrow { get; set; } = null!;

        [ForeignKey("RaisedByUserId")]
        public virtual User RaisedByUser { get; set; } = null!;
    }
}