using System.Collections.Generic;
using PicklePlay.Models;

namespace PicklePlay.Models.ViewModels
{
    public class CommunicationHubViewModel
    {
        public List<TeamInvitation> PendingTeamInvitations { get; set; } = new List<TeamInvitation>();
        public List<Friendship> PendingFriendRequests { get; set; } = new List<Friendship>();
        public List<Friendship> Friends { get; set; } = new List<Friendship>();
        public List<CommunityInvitation> PendingCommunityInvitations { get; set; } = new List<CommunityInvitation>();

        public List<Notification> ?GeneralNotifications { get; set; }
    }

    public class FriendListViewModel
    {
        public List<FriendItem> Friends { get; set; } = new List<FriendItem>();
        public List<FriendRequestItem> PendingRequests { get; set; } = new List<FriendRequestItem>();
        public List<FriendRequestItem> SentRequests { get; set; } = new List<FriendRequestItem>();
    }

    public class FriendItem
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? ProfilePicture { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastSeen { get; set; }
        public int UnreadMessageCount { get; set; }
        public string? LastMessage { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public int FriendshipId { get; set; }
    }

    public class FriendRequestItem
    {
        public int FriendshipId { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? ProfilePicture { get; set; }
        public DateTime RequestedAt { get; set; }
        public bool IsIncoming { get; set; } // true = received, false = sent
    }

    public class ChatViewModel
    {
        public int FriendId { get; set; }
        public string FriendUsername { get; set; } = string.Empty;
        public string? FriendProfilePicture { get; set; }
        public bool IsOnline { get; set; }
        public List<ChatMessageItem> Messages { get; set; } = new List<ChatMessageItem>();
    }

    public class ChatMessageItem
    {
        public int MessageId { get; set; }
        public int SenderId { get; set; }
        public int ReceiverId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
        public bool IsMine { get; set; } // Is message sent by current user
    }

    public class NotificationViewModel
    {
        public int NotificationId { get; set; }
        public NotificationType Type { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ActionUrl { get; set; }
        public int? RelatedUserId { get; set; }
        public string? RelatedUsername { get; set; }
        public string? RelatedUserProfilePicture { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public string TimeAgo { get; set; } = string.Empty;
    }
}
