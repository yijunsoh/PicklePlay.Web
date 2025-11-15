using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class Award
    {
        [Key]
        public int AwardId { get; set; }

        [Required]
        public int ScheduleId { get; set; }

        [ForeignKey("ScheduleId")]
        public Schedule? Schedule { get; set; }

        [Required]
        [MaxLength(200)]
        public string AwardName { get; set; } = string.Empty;

        [Required]
        public AwardType AwardType { get; set; } = AwardType.Trophy;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        public AwardPosition Position { get; set; } = AwardPosition.Champion;
      
        public int? TeamId { get; set; }

        [ForeignKey("TeamId")]
        public Team? Team { get; set; }

        public DateTime AwardedDate { get; set; } = DateTime.UtcNow;

        public int? SetByUserId { get; set; }

        [ForeignKey("SetByUserId")]
        public User? SetByUser { get; set; }
    }

    public enum AwardType
    {
        Trophy,
        Medal,
        Ribbon,
        Crown,
        Star,
        Shirt,
        Shoe,
        Ticket
    }

    public enum AwardPosition
    {
        Champion = 1,
        FirstRunnerUp = 2,
        SecondRunnerUp = 3
    }
}