using System;
using System.ComponentModel.DataAnnotations;

namespace PicklePlay.Models
{
    public class ScheduleCreateViewModel
    {
        [Display(Name = "Event Tag")]
        public EventTag EventTag { get; set; } = EventTag.None;

        [Display(Name = "Start Time")]
        [Required]
        public DateTime StartTime { get; set; } = DateTime.Today.AddHours(18);

        [Required]
        public Duration Duration { get; set; } = Duration.H2;

        [Required]
        [StringLength(255)]
        public string Location { get; set; } = "";

        [Display(Name = "Number of Players")]
        [Range(2, 200)]
        public int NumPlayer { get; set; } = 8;
        
        [Required]
        public Privacy Privacy { get; set; } = Privacy.Public;

        [Display(Name = "Game Fee")]
        [Required]
        public FeeType FeeType { get; set; } = FeeType.PerPerson;

        [Display(Name = "Fee Amount (RM)")]
        [Range(0, 10000)]
        public decimal? FeeAmount { get; set; }

        [Display(Name = "Game Feature")]
        public GameFeature GameFeature { get; set; } = GameFeature.Basic;

        [Display(Name = "Min Rank")]
        public decimal? MinRankRestriction { get; set; }
        
        [Display(Name = "Max Rank")]
        public decimal? MaxRankRestriction { get; set; }

        [Required]
        public GenderRestriction GenderRestriction { get; set; } = GenderRestriction.None;

        [Display(Name = "Age Group")]
        [Required]
        public AgeGroupRestriction AgeGroupRestriction { get; set; } = AgeGroupRestriction.Adult;

        [Display(Name = "Cancellation Freeze")]
        [Required]
        public CancellationFreeze CancellationFreeze { get; set; } = CancellationFreeze.None;


        [Display(Name = "Host Role")]
        [Required]
        public HostRole HostRole { get; set; } = HostRole.HostAndPlay;

        [Display(Name = "Game Name")]
        [Required]
        [StringLength(100)]
        public string GameName { get; set; } = "";

        [Display(Name = "Game Description")]
        [StringLength(1000)]
        public string? Description { get; set; }

        public int? CommunityId { get; set; }
    }
}