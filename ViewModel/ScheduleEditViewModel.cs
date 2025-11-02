using System;
using System.ComponentModel.DataAnnotations;

namespace PicklePlay.Models
{
    // Combine fields needed for editing either OneOff or Recurring
    public class ScheduleEditViewModel
    {
        [Required]
        public int ScheduleId { get; set; } // Keep track of which schedule we're editing

        [Required]
        public ScheduleType ScheduleType { get; set; } // To know which fields to show/use

        // --- Game Info ---
        [Display(Name = "Game Name")]
        [Required]
        [StringLength(100)]
        public string GameName { get; set; } = "";

        [Display(Name = "Game Description")]
        [StringLength(1000)]
        public string? Description { get; set; }

        // --- Common Fields ---
        [Display(Name = "Event Tag")]
        public EventTag EventTag { get; set; } = EventTag.None;

        [Required]
        [StringLength(255)]
        public string Location { get; set; } = "";

        [Required]
        public Duration Duration { get; set; } = Duration.H2;

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

        // --- One-Off Specific ---
        [Display(Name = "Start Date & Time")]
        
        public DateTime? StartTime { get; set; } // Use DateTime for OneOff edit

        public Repeat Repeat { get; set; } = Repeat.None; // Only for OneOff

        // --- Recurring Specific ---
        [Display(Name = "Day of the Week")]
        [Required(ErrorMessage = "Please select at least one day.")] // Required for Recurring
        public List<RecurringWeek> RecurringWeek { get; set; } = new();

        [Display(Name = "Start Time")]
        
        public TimeOnly? RecurringStartTime { get; set; } // Use TimeOnly for Recurring edit

        [Display(Name = "Auto-Create When")]
        [Required] // Required for Recurring
        public AutoCreateWhen? AutoCreateWhen { get; set; } = Models.AutoCreateWhen.B2d;
    }
}