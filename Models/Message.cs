using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PicklePlay.Helpers;

namespace PicklePlay.Models
{
    public class Message
    {
        [Key]
        public int MessageId { get; set; }

        [Required]
        public int SenderId { get; set; }

        [Required]
        public int ReceiverId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Content { get; set; } = string.Empty;

         private DateTime _sentAt = DateTimeHelper.GetMalaysiaTime(); // ⬅️ CHANGED
        public DateTime SentAt 
        { 
            get => _sentAt; 
            set => _sentAt = value; 
        }

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        public bool IsDeleted { get; set; } = false;

        // Navigation properties
        [ForeignKey("SenderId")]
        public virtual User? Sender { get; set; }

        [ForeignKey("ReceiverId")]
        public virtual User? Receiver { get; set; }
    }

    public enum NotificationType
    {
        FriendRequest = 0,
        FriendRequestAccepted = 1,
        Message = 2,
        GameInvite = 3,
        TeamInvite = 4,
        CompetitionUpdate = 5,
        Endorsement = 6
    }
}