using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class ManageTeamViewModel
    {
        public int ScheduleId { get; set; }
        public string ?CompetitionName { get; set; }

        public List<Team> OnHoldTeams { get; set; } = new List<Team>();
        public List<Team> PendingTeams { get; set; } = new List<Team>();
        public List<Team> ConfirmedTeams { get; set; } = new List<Team>();
        public bool IsOrganizer { get; set; }
        public bool IsFreeCompetition { get; set; } 
        public int? CurrentUserId { get; set; }
        public int MaxTeams { get; set; }
    }
}