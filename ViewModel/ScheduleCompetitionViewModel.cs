using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PicklePlay.Models
{
    public class ScheduleCompetitionViewModel
    {
        public int ScheduleId { get; set; }
        public string? ExistingImageUrl { get; set; }
        // --- Schedule Fields ---
        [Display(Name = "Competition Name")]
        [Required]
        [StringLength(100)]
        public string GameName { get; set; } = "";

        // REMOVED: EventTag

        [Display(Name = "Description")]
        [StringLength(1000)]
        public string? Description { get; set; }

        [Required]
        [StringLength(255)]
        public string Location { get; set; } = "";

        [Display(Name = "Start Date & Time")]
        [Required]
        public DateTime StartTime { get; set; } = DateTime.Today.AddDays(7).AddHours(9);

        [Display(Name = "End Date & Time")]
        [Required]
        public DateTime EndTime { get; set; } = DateTime.Today.AddDays(7).AddHours(17);

        [Display(Name = "Approximate Start Time (Optional)")]
        public DateTime? ApproxStartTime { get; set; }

        [Display(Name = "Registration Opens")]
        [Required]
        public DateTime RegOpen { get; set; } = DateTime.Today.AddDays(1);

        [Display(Name = "Registration Closes")]
        [Required]
        public DateTime RegClose { get; set; } = DateTime.Today.AddDays(6);

        [Display(Name = "Early Bird Deadline (Optional)")]
        public DateTime? EarlyBirdClose { get; set; }

        [Display(Name = "Number of Teams")]
        [Required]
        [Range(2, 200)]
        public int NumTeam { get; set; } = 16;

        [Display(Name = "Min Rank")]
        public decimal? MinRankRestriction { get; set; }

        [Display(Name = "Max Rank")]
        public decimal? MaxRankRestriction { get; set; }

        [Required]
        public GenderRestriction GenderRestriction { get; set; } = GenderRestriction.None;

        [Display(Name = "Age Group")]
        [Required]
        public AgeGroupRestriction AgeGroupRestriction { get; set; } = AgeGroupRestriction.Adult;

        [Display(Name = "Fee Type")]
        [Required]
        // We'll map "PerTeam" to FeeType.PerPerson conceptually here.
        // Or you could add FeeType.PerTeam if modifying the enum is acceptable.
        // Let's stick with mapping PerTeam -> PerPerson for now.
        public FeeType FeeType { get; set; } = FeeType.PerPerson; // Default to Per Team (mapped to PerPerson)

        [Display(Name = "Fee Amount (RM per Team)")]
        [Range(0, 10000)]
        public decimal? FeeAmount { get; set; } // Required only if FeeType is PerPerson (representing Per Team)

        [Required]
        public Privacy Privacy { get; set; } = Privacy.Public;

        [Display(Name = "Cancellation Freeze")]
        [Required]
        public CancellationFreeze CancellationFreeze { get; set; } = CancellationFreeze.B24hr;

        [Display(Name = "Competition Poster/Banner")]
        public IFormFile? PosterImage { get; set; }


    }
}