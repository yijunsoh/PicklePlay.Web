using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class CommunityChatMessage
    {
        [Key]
        public int MessageId { get; set; }

        [Required]
        public int CommunityId { get; set; }

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
        [ForeignKey("CommunityId")]
        public virtual Community? Community { get; set; }

        [ForeignKey("SenderId")]
        public virtual User? Sender { get; set; }

        [ForeignKey("DeletedByUserId")]
        public virtual User? DeletedBy { get; set; }
    }
}