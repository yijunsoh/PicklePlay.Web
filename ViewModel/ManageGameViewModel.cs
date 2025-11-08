using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class ManageGameViewModel
    {
        public int ScheduleId { get; set; }
        public string ?GameName { get; set; }
        public List<ScheduleParticipant>? Participants { get; set; }

        public bool RequireOrganizerApproval { get; set; }
    }
}