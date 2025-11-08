using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class ViewEndorsementViewModel
    {
        public int ScheduleId { get; set; }
        public string ?ScheduleName { get; set; }
        public List<EndorsementGroup> ?SkillGroups { get; set; }
        public List<EndorsementGroup>? PersonalityGroups { get; set; }
        
        public List<EndorsementSummary> ?MySkillAwards { get; set; }
        public List<EndorsementSummary> ?MyPersonalityAwards { get; set; }
    }

    public class EndorsementGroup
    {
        public string ?EndorsementName { get; set; }
        public List<EndorsementRecipient> ?Recipients { get; set; }
    }

    public class EndorsementRecipient
    {
        public int UserId { get; set; }
        public string? Username { get; set; }
        public string? ProfilePicture { get; set; }
        public int Count { get; set; } // Total endorsements for this type
        public bool IsCurrentUser { get; set; } // Is this me?
        public bool IsEndorsedByMe { get; set; } // Did I endorse this person for this?
    }
    
    public class EndorsementSummary
    {
        public string ?EndorsementName { get; set; }
        public int Count { get; set; }
    }
}