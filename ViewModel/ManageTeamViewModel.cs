using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class ManageTeamViewModel
    {
        public int ScheduleId { get; set; }
        public string ?CompetitionName { get; set; }
        public List<Team> ?PendingTeams { get; set; }
        public List<Team> ?ConfirmedTeams { get; set; }
        public bool IsOrganizer { get; set; }
        public int? CurrentUserId { get; set; }
    }
}