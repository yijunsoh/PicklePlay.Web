using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class ScheduleParticipant
    {
        [Key]
        public int SP_Id { get; set; }

        [Required]
        public int ScheduleId { get; set; }
        [ForeignKey("ScheduleId")]
        public virtual Schedule? Schedule { get; set; }

        [Required]
        public int UserId { get; set; } 
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [Required]
        public ParticipantRole Role { get; set; }

        [Required]
        public ParticipantStatus Status { get; set; }

         // ⬇️ ADD THIS PROPERTY
        public DateTime? JoinedDate { get; set; }

        // ⬇️ ADD THIS PROPERTY (optional but useful)
        public DateTime? RequestDate { get; set; }

        // ⬇️ ADD THIS PROPERTY (optional but useful)
        public DateTime? ResponseDate { get; set; }
    }
}