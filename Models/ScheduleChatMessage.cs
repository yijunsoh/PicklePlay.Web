using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class ScheduleChatMessage
    {
        [Key]
        public int MessageId { get; set; }

        [Required]
        public int ScheduleId { get; set; }

        [Required]
        public int SenderId { get; set; }

        [Required]
        [StringLength(2000)]
        public string Content { get; set; } = string.Empty;

        [Required]
        public DateTime SentAt { get; set; }

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        public int? DeletedByUserId { get; set; }

        // Navigation properties
        [ForeignKey("ScheduleId")]
        public virtual Schedule? Schedule { get; set; }

        [ForeignKey("SenderId")]
        public virtual User? Sender { get; set; }

        [ForeignKey("DeletedByUserId")]
        public virtual User? DeletedBy { get; set; }
    }
}