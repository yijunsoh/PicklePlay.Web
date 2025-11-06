using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    // --- ViewModel for Pool Play Page ---
    public class DrawPoolPlayViewModel
    {
        public int ScheduleId { get; set; }
        public string? CompetitionName { get; set; }
        public List<Pool> Pools { get; set; } = new List<Pool>();
        public List<Team> UnassignedTeams { get; set; } = new List<Team>();

        // This property will be populated by the form post
        // The key will be the TeamId, the value will be the selected PoolId
        public Dictionary<int, int>? TeamPoolSelections { get; set; }
    }

    // --- ViewModel for Elimination Page ---
    public class DrawEliminationViewModel
    {
        public int ScheduleId { get; set; }
        public string? CompetitionName { get; set; }
        public List<Team> Teams { get; set; } = new List<Team>();
        public int TotalSeeds { get; set; }

        // *** This property was added ***
        public bool HasThirdPlaceMatch { get; set; }

        // This property will be populated by the form post
        // The key will be the Seed number (1, 2, 3...), the value will be the selected TeamId
        public Dictionary<int, int>? SeedAssignments { get; set; }
    }
}