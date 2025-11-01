using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("Transaction")]
    public class Transaction
    {
        [Key]
        [Column("transaction_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int TransactionId { get; set; }

        [Required]
        [Column("wallet_id")]
        public int WalletId { get; set; }

        [Column("escrow_id")]
        public int? EscrowId { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("transaction_type")]
        public string TransactionType { get; set; } = null!; // TopUp, Withdraw, Escrow_Hold, Refund, Released

        [Required]
        [Column("amount", TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        // PAYMENT FIELDS
        [Required]
        [MaxLength(20)]
        [Column("payment_method")]
        public string PaymentMethod { get; set; } = "Wallet";

        [Required]
        [MaxLength(20)]
        [Column("payment_status")]
        public string PaymentStatus { get; set; } = "Pending";

        [MaxLength(100)]
        [Column("payment_gateway_id")]
        public string? PaymentGatewayId { get; set; }

        [MaxLength(50)]
        [Column("card_last_four")]
        public string? CardLastFour { get; set; }

        [Required]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("payment_completed_at")]
        public DateTime? PaymentCompletedAt { get; set; }

        // Navigation properties
        [ForeignKey("WalletId")]
        public virtual Wallet Wallet { get; set; } = null!;

        [ForeignKey("EscrowId")]
        public virtual Escrow? Escrow { get; set; }
    }
}