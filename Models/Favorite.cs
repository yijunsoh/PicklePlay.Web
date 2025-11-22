// Favorite.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Helpers;

namespace PicklePlay.Models
{
    [Table("Favorite")]
    [Index(nameof(UserId), nameof(TargetUserId), IsUnique = true)]
    public class Favorite
    {
        [Key]
        [Column("favorite_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FavoriteId { get; set; }

        [Required]
        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [Column("target_user_id")]
        public int TargetUserId { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTimeHelper.GetMalaysiaTime();

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [ForeignKey("TargetUserId")]
        public virtual User TargetUser { get; set; } = null!;
    }
}