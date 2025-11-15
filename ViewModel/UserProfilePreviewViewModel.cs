using PicklePlay.Models;
using PicklePlay.Models.ViewModels;

namespace PicklePlay.ViewModels
{
    public class UserProfilePreviewViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? ProfileImagePath { get; set; }
        public string? Gender { get; set; }
        public int? Age { get; set; }
        public string? Location { get; set; }
        public string? Bio { get; set; }
        
        // Social Stats
        public int EndorsementCount { get; set; }
        public int AchievementCount { get; set; }
        public int FriendCount { get; set; }
        
        // Collections
        public List<EndorsementPreviewItem> RecentEndorsements { get; set; } = new List<EndorsementPreviewItem>();
        public List<AchievementItem> RecentAchievements { get; set; } = new List<AchievementItem>();
        
        // Status flags
        public bool IsFriend { get; set; }
        public bool HasPendingFriendRequest { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public class EndorsementPreviewItem
    {
        public int EndorsementId { get; set; }
        public string EndorserName { get; set; } = string.Empty;
        public string? EndorserImage { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}