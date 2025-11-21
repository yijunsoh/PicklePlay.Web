using System.ComponentModel.DataAnnotations;

namespace PicklePlay.Models
{
    public class CompetitionSetupViewModel
    {
        [Required]
        public int ScheduleId { get; set; }

        [Required]
        [Display(Name = "Competition Format")]
        public CompetitionFormat Format { get; set; }

        public string? GameName { get; set; } // To display on the page

        // --- Pool Play Specific ---
        [Display(Name = "Number of Pools")]
        [Range(1, 64)] // Example range
        public int NumPool { get; set; } = 4; // Default from DB

        [Display(Name = "Winners Advancing per Pool")]
        [Range(1, 16)] // Example range
        public int WinnersPerPool { get; set; } = 1; // Default from DB

        [Display(Name = "Standing Calculation Method")]
        public StandingCalculation StandingCalculation { get; set; } = StandingCalculation.WinLossPoints; // Default from DB

        // Points (Only visible if StandingCalculation == WinLossPoints)
        [Display(Name = "Points for Standard Win")]
        public int StandardWin { get; set; } = 3;
        [Display(Name = "Points for Standard Loss")]
        public int StandardLoss { get; set; } = 0;
        [Display(Name = "Points for Tie-Break Win")]
        public int TieBreakWin { get; set; } = 3;
        [Display(Name = "Points for Tie-Break Loss")]
        public int TieBreakLoss { get; set; } = 1;
        [Display(Name = "Points for Draw")]
        public int Draw { get; set; } = 1;

        // --- Elimination Specific ---
        [Display(Name = "Include 3rd Place Match?")]
        public bool ThirdPlaceMatch { get; set; } = true; // Default from DB (mapped from tinyint 0)

        // --- Round Robin Specific ---
        [Display(Name = "Play Each Team Twice (Double Round Robin)?")]
        public bool DoublePool { get; set; } = false; // Default from DB (mapped from tinyint 1)

        // --- Common / Elimination / Round Robin ---
        [Display(Name = "Match Rules / Details")]
        [StringLength(1000)]
        public string? MatchRule { get; set; } // Default from DB


    }
}