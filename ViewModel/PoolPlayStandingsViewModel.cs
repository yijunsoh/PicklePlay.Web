using PicklePlay.Application.Services;
using PicklePlay.Models;

namespace PicklePlay.Models.ViewModels
{
    public class PoolPlayStandingsViewModel
    {
        public int ScheduleId { get; set; }
        public List<PoolStandingGroup> PoolGroups { get; set; } = new();
        public StandingCalculation CalculationMethod { get; set; }
        public Competition? Competition { get; set; }
        public bool IsPoolStageComplete { get; set; }
    }

    public class PoolStandingGroup
    {
        public string PoolName { get; set; } = string.Empty;
        public int PoolId { get; set; }
        public List<TeamStanding> Standings { get; set; } = new();
        public int AdvancingTeams { get; set; }
        public bool IsComplete { get; set; }
    }
}