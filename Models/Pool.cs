using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    [Table("Pool")]
    public class Pool
    {
        [Key]
        public int PoolId { get; set; }

        [Required]
        public int ScheduleId { get; set; }
        [ForeignKey("ScheduleId")]
        public virtual Schedule ?Schedule { get; set; }

        [Required]
        [MaxLength(100)]
        public string ?PoolName { get; set; }

        // Navigation property for all teams assigned to this pool
        public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
    }
}