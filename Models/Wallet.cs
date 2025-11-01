using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("Wallet")]
    public class Wallet
    {
        [Key]
        [Column("wallet_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int WalletId { get; set; }

        [Required]
        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [Column("wallet_balance", TypeName = "decimal(10,2)")]
        public decimal WalletBalance { get; set; } = 0;

        [Required]
        [Column("escrow_balance", TypeName = "decimal(10,2)")]
        public decimal EscrowBalance { get; set; } = 0;

        [Required]
        [Column("total_spent", TypeName = "decimal(10,2)")]
        public decimal TotalSpent { get; set; } = 0;

        [Required]
        [Column("last_updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}