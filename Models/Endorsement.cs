using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class Endorsement
    {
        [Key]
        public int EndorsementId { get; set; }

        [Required]
        [ForeignKey("Schedule")]
        public int ScheduleId { get; set; }
        public virtual Schedule ?Schedule { get; set; }

        [Required]
        [ForeignKey("GiverUser")]
        public int GiverUserId { get; set; } // The user giving the endorsement
        public virtual User ?GiverUser { get; set; }

        [Required]
        [ForeignKey("ReceiverUser")]
        public int ReceiverUserId { get; set; } // The user receiving the endorsement
        public virtual User ?ReceiverUser { get; set; }

        public PersonalityEndorsement Personality { get; set; } = PersonalityEndorsement.None;

        public SkillEndorsement Skill { get; set; } = SkillEndorsement.None;

        public DateTime DateGiven { get; set; } = DateTime.Now;
    }

    public enum PersonalityEndorsement
    {
        None,
        Sportsmanship,
        Heart,
        Mentoring,
        Leadership,
        Friendly,
        Focused,
        Smart,
        Helpful,
        Competitive,
        Funny,
        Enthusiastic,
        Organized,
        Fair
    }

    public enum SkillEndorsement
    {
        None,
        Dink,
        Overhead,
        Volley,
        Driving,
        Drop,
        Reset,
        Lobbing,
        Defense,
        Serving,
        Returning,
        Poaching,
        Erne,
        ATP
    }
}