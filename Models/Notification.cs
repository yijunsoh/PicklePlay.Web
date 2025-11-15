using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PicklePlay.Helpers;

namespace PicklePlay.Models
{
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }

        [Required]
        [ForeignKey("User")]
        public int UserId { get; set; } // The user who *receives* the notification
        public virtual User ?User { get; set; }
        
        [Required]
        public NotificationType Type { get; set; }

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public string? LinkUrl { get; set; } // Optional: e.g., /Schedule/Details/5

        public string? ActionUrl { get; set; }

        public int? RelatedUserId { get; set; } // For friend requests

        public int? RelatedEntityId { get; set; } // For generic references

        public bool IsRead { get; set; } = false;

        public DateTime DateCreated { get; set; } = DateTime.Now;

        private DateTime _createdAt = DateTimeHelper.GetMalaysiaTime(); // ⬅️ CHANGED
        public DateTime CreatedAt 
        { 
            get => _createdAt; 
            set => _createdAt = value; 
        }



        public DateTime? ReadAt { get; set; }


        [ForeignKey("RelatedUserId")]
        public virtual User? RelatedUser { get; set; }
    }
    
    
    

}