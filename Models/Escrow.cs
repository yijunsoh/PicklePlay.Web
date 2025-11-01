using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("Escrow")]
    public class Escrow
    {
        [Key]
        [Column("escrow_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EscrowId { get; set; }

        [Required]
        [Column("schedule_id")]
        public int ScheduleId { get; set; }

        [Required]
        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Pending";

        [Required]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
        public virtual ICollection<EscrowDispute> EscrowDisputes { get; set; } = new List<EscrowDispute>();
    }
}