using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class RRStandingsViewModel
    {
        public int ScheduleId { get; set; }
        public List<RRTeamStanding> Standings { get; set; } = new();
    }

    public class RRTeamStanding
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string? TeamIconUrl { get; set; }

        public int GamesPlayed { get; set; }
        public int MatchesWon { get; set; }
        public int MatchesLost { get; set; }

        public int GamesWon { get; set; }
        public int GamesLost { get; set; }
        public int ScoreDifference { get; set; }
    }
}