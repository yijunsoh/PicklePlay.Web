using PicklePlay.Models;
using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class StartCompetitionConfirmViewModel
    {
        public Schedule ?Schedule { get; set; }
        public Competition ?Competition { get; set; }
        public DrawPoolPlayViewModel ?PoolDraw { get; set; }
        public DrawEliminationViewModel ?EliminationDraw { get; set; }

        public int ConfirmedTeamCount { get; set; }
    }

    public class MatchListingViewModel
    {
        public List<Match> ?Matches { get; set; }
        public bool IsOrganizer { get; set; }
        public int ScheduleId { get; set; }
    }
}