using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("Refund_Request")]

    public class RefundRequest
    {
        [Key]
    [Column("refund_id")]
    public int RefundId { get; set; }

    [Column("escrow_id")]
    public int EscrowId { get; set; }
    public Escrow? Escrow { get; set; }

    // Required FK
    [Column("UserId")]
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Business logic column
    [Column("reported_by")]
    public int ReportedBy { get; set; }

    [Column("refund_reason", TypeName="text")]
    public string? RefundReason { get; set; }

    [Column("admin_decision")]
    public string AdminDecision { get; set; } = "Pending";
    
    [Column("admin_note", TypeName="text")]
    public string? AdminNote { get; set; }

    [Column("decision_date")]
    public DateTime? DecisionDate { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
    }
}