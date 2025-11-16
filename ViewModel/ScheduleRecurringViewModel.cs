using System;
using System.ComponentModel.DataAnnotations;

namespace PicklePlay.Models
{
    public class ScheduleRecurringViewModel
    {
        // --- NEW RECURRING-SPECIFIC FIELDS ---
        [Display(Name = "Day of the Week")]
        [Required(ErrorMessage = "Please select at least one day.")]
        // Changed to a List to accept multiple values
        public List<RecurringWeek> RecurringWeek { get; set; } = new();

        [Display(Name = "Auto-Create When")]
        [Required]
        public AutoCreateWhen AutoCreateWhen { get; set; } = AutoCreateWhen.B2d;

        [Required(ErrorMessage = "Please select an end date for the recurrence.")]
        [DataType(DataType.Date)]
        [Display(Name = "Recur Until")]
        public DateTime? RecurringEndDate { get; set; }
        
        [Display(Name = "Start Time")]
        [Required]
        public TimeOnly StartTime { get; set; } = new TimeOnly(18, 00); // 6:00 PM

        // --- COMMON FIELDS (MOVED GAME INFO UP) ---
        [Display(Name = "Game Name")]
        [Required]
        [StringLength(100)]
        public string GameName { get; set; } = "";

        [Display(Name = "Game Description")]
        [StringLength(1000)]
        public string? Description { get; set; }

        // --- OTHER FIELDS ---
        [Display(Name = "Event Tag")]
        public EventTag EventTag { get; set; } = EventTag.None;

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

        public int? CommunityId { get; set; }
    }
}