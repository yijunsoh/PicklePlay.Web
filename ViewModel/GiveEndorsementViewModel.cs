using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class GiveEndorsementViewModel
    {
        public int ScheduleId { get; set; }
        public string ?ScheduleName { get; set; }
        
        // This list will hold all the players and their selected endorsements
        public List<ParticipantEndorsement> ParticipantsToEndorse { get; set; }

        public GiveEndorsementViewModel()
        {
            ParticipantsToEndorse = new List<ParticipantEndorsement>();
        }
    }

    // This sub-model is used for the form binding
    public class ParticipantEndorsement
    {
        public int ReceiverUserId { get; set; }
        public string ?Username { get; set; }
        public string? ProfilePicture { get; set; }

        // These properties will be bound from the dropdowns on submit
        public PersonalityEndorsement SelectedPersonality { get; set; }
        public SkillEndorsement SelectedSkill { get; set; }

        // These will be used to show/hide the dropdowns in the view
        public bool HasExistingPersonality { get; set; }
        public bool HasExistingSkill { get; set; }
    }
}