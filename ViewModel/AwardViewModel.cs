using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class AwardViewModel
    {
        public int ScheduleId { get; set; }
        public string CompetitionTitle { get; set; } = string.Empty;
        public CompetitionFormat Format { get; set; }
        public ScheduleStatus Status { get; set; }
        public bool IsOrganizer { get; set; }
        
        // Award settings
        public string AwardName { get; set; } = string.Empty;
        public AwardType AwardType { get; set; } = AwardType.Trophy;
        public string? Description { get; set; }
        
        // Winners
        public AwardWinnerInfo? Champion { get; set; }
        public AwardWinnerInfo? FirstRunnerUp { get; set; }
        public AwardWinnerInfo? SecondRunnerUp { get; set; }
        
        public bool HasAwards { get; set; }
        public bool HasThirdPlace { get; set; } 
    }

    public class AwardWinnerInfo
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public List<string> PlayerNames { get; set; } = new();
        public AwardType AwardType { get; set; }
        public string AwardName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime AwardedDate { get; set; }
        public AwardPosition Position { get; set; }
    }

    public class UserAchievementViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public List<AchievementItem> Achievements { get; set; } = new();
    }

    public class AchievementItem
    {
        public int AwardId { get; set; }
        public string AwardName { get; set; } = string.Empty;
        public AwardType AwardType { get; set; }
        public string? Description { get; set; }
        public AwardPosition Position { get; set; }
        public string PositionName { get; set; } = string.Empty;
        public DateTime AwardedDate { get; set; }
        public string CompetitionTitle { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
    }
}