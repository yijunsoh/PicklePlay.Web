namespace PicklePlay.Models.ViewModels
{
    public class PoolPlayMatchListingViewModel
    {
        public int ScheduleId { get; set; }
        public List<PoolMatchGroup> PoolGroups { get; set; } = new();
        public PlayoffMatchGroup? PlayoffGroup { get; set; }
        public bool IsPoolStageComplete { get; set; }
        public bool CanAdvanceToPlayoff { get; set; }
        public bool IsOrganizer { get; set; } // *** ADD THIS LINE ***
    }

    public class PoolMatchGroup
    {
        public string PoolName { get; set; } = string.Empty;
        public List<Match> Matches { get; set; } = new();
        public bool IsComplete { get; set; }
    }

    public class PlayoffMatchGroup
    {
        public List<Match> Matches { get; set; } = new();
        public bool HasStarted { get; set; }
        public string Status { get; set; } = "Pending";
    }
}