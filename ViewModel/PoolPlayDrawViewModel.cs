using PicklePlay.Models;

namespace PicklePlay.Models.ViewModels
{
    public class PoolPlayDrawViewModel
    {
        public int ScheduleId { get; set; }
        public List<Pool> Pools { get; set; } = new();
        public int WinnersPerPool { get; set; }
        public bool AllPoolsComplete { get; set; }
        public string PlayoffStatus { get; set; } = "Pending"; // Pending, Active, Completed
        public bool HasThirdPlaceMatch { get; set; }
    }
}