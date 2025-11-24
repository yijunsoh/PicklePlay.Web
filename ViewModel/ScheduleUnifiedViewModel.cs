using PicklePlay.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PicklePlay.Models
{
    public class ScheduleUnifiedViewModel
    {
        // Basic Info (Always Required)
        [Required(ErrorMessage = "Game name is required")]
        [StringLength(100, ErrorMessage = "Game name cannot exceed 100 characters")]
        [Display(Name = "Game Name")]
        public string GameName { get; set; } = string.Empty;

        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Location is required")]
        [Display(Name = "Location")]
        public string Location { get; set; } = string.Empty;

        // Scheduling (Required for OneOff, Optional for Recurring)
        [Display(Name = "Start Date & Time")]
        public DateTime? StartTime { get; set; }

        [Required]
        [Display(Name = "Duration")]
        public Duration Duration { get; set; } = Duration.H2;

        // Recurring Options (Optional - determines if Recurring or OneOff)
        [Display(Name = "Repeat Weekly")]
        public List<RecurringWeek>? RecurringWeek { get; set; }

        [Display(Name = "Repeat Until")]
        public DateTime? RecurringEndDate { get; set; }


        // Players & Privacy
        [Required]
        [Range(2, 100, ErrorMessage = "Number of players must be between 2 and 100")]
        [Display(Name = "Number of Players")]
        public int NumPlayer { get; set; } = 8;

        [Required]
        [Display(Name = "Privacy")]
        public Privacy Privacy { get; set; } = Privacy.Public;

        // Fee
        [Required]
        [Display(Name = "Fee Type")]
        public FeeType FeeType { get; set; } = FeeType.Free;

        [Display(Name = "Fee Amount (RM)")]
        [Range(0, 10000, ErrorMessage = "Fee must be between 0 and 10,000")]
        public decimal? FeeAmount { get; set; }

        // Optional Features (Collapsed by default)
        [Display(Name = "Event Tag")]
        public EventTag EventTag { get; set; } = EventTag.None;

        [Display(Name = "Minimum RANK")]
        [Range(0, 8, ErrorMessage = "Minimum RANK must be between 0.0 and 8.0")]
        [RegularExpression(@"^\d{1}\.\d{0,3}$", ErrorMessage = "RANK must have maximum 3 decimal places (e.g., 2.500)")]
        public decimal? MinRankRestriction { get; set; }

        [Display(Name = "Maximum RANK")]
        [Range(0, 8, ErrorMessage = "Maximum RANK must be between 0.0 and 8.0")]
        [RegularExpression(@"^\d{1}\.\d{0,3}$", ErrorMessage = "RANK must have maximum 3 decimal places (e.g., 5.000)")]
        public decimal? MaxRankRestriction { get; set; }

        [Display(Name = "Gender Restriction")]
        public GenderRestriction GenderRestriction { get; set; } = GenderRestriction.None;

        [Display(Name = "Age Group")]
        public AgeGroupRestriction AgeGroupRestriction { get; set; } = AgeGroupRestriction.Adult;

        [Display(Name = "Cancellation Policy")]
        public CancellationFreeze CancellationFreeze { get; set; } = CancellationFreeze.None;

        [Display(Name = "Host Role")]
        public HostRole HostRole { get; set; } = HostRole.HostAndPlay;

        // Computed property: Is this a recurring schedule?
        public bool IsRecurring => RecurringWeek != null && RecurringWeek.Any();
    }
}