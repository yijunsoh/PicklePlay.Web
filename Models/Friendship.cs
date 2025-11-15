using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PicklePlay.Helpers;

namespace PicklePlay.Models
{
    [Table("Friendship")]
    public class Friendship
    {
        [Key]
        public int FriendshipId { get; set; }

        // The user who sent the request
        [Required]
        public int UserOneId { get; set; }
        [ForeignKey("UserOneId")]
        public virtual User ?UserOne { get; set; }

        // The user who received the request
        [Required]
        public int UserTwoId { get; set; }
        [ForeignKey("UserTwoId")]
        public virtual User ?UserTwo { get; set; }

        [Required]
        public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

        private DateTime _requestDate = DateTimeHelper.GetMalaysiaTime();
        [Required]
        public DateTime RequestDate 
        { 
            get => _requestDate; 
            set => _requestDate = value; 
        }

        // Date when the friend request was accepted
        public DateTime? AcceptedDate { get; set; }
    }

    public enum FriendshipStatus
    {
        Pending,  // Request sent, waiting for response
        Accepted, // Both users are friends
        Declined, // The request was declined
        Blocked   // One user blocked the other
    }
}