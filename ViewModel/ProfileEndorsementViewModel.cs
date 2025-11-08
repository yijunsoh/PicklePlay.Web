using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    // This is the main model for the partial view
    public class ProfileEndorsementViewModel
    {
        public List<EndorsementSummary> TopSkills { get; set; }
        public List<EndorsementSummary> AllSkills { get; set; }
        public List<EndorsementSummary> TopPersonalities { get; set; }
        public List<EndorsementSummary> AllPersonalities { get; set; }

        public ProfileEndorsementViewModel()
        {
            TopSkills = new List<EndorsementSummary>();
            AllSkills = new List<EndorsementSummary>();
            TopPersonalities = new List<EndorsementSummary>();
            AllPersonalities = new List<EndorsementSummary>();
        }
    }

    // We can reuse the summary class from the "See Endorsements" VM
    // If you placed EndorsementSummary in its own file, you don't need to add it here.
    // public class EndorsementSummary { ... } 
}