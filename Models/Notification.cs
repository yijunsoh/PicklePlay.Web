using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        public string ?Message { get; set; }

        public string? LinkUrl { get; set; } // Optional: e.g., /Schedule/Details/5

        public bool IsRead { get; set; } = false;

        public DateTime DateCreated { get; set; } = DateTime.Now;
    }
}