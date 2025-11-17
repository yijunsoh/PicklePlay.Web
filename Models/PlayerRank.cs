using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class PlayerRank
    {
        [Key]
        public int RankId { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        // Core Rating (0.000 - 8.000+, displayed with 3 decimals)
        [Required]
        [Range(0.0, 10.0)]
        [Column(TypeName = "decimal(5,3)")]
        public decimal Rating { get; set; } = 0.0m;

        // Reliability Score (0-100%)
        [Required]
        [Range(0, 100)]
        [Column(TypeName = "decimal(5,2)")]
        public decimal ReliabilityScore { get; set; } = 0.0m;

        // Rank Status
        [Required]
        public RankStatus Status { get; set; } = RankStatus.NR;

        // Match Statistics
        public int TotalMatches { get; set; } = 0;
        public int SinglesMatches { get; set; } = 0;
        public int DoublesMatches { get; set; } = 0;
        public int Wins { get; set; } = 0;
        public int Losses { get; set; } = 0;

        // Network Diversity Metrics
        public int UniquePartners { get; set; } = 0;
        public int UniqueOpponents { get; set; } = 0;
        public int UniqueCommunities { get; set; } = 0;

        // Timestamps
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public DateTime? LastMatchDate { get; set; }

        // Calculated Properties
        [NotMapped]
        public string DisplayRating
        {
            get
            {
                // ⬇️ FIX: Show "NR" only if no matches played, not if rating is 2.500
                if (Status == RankStatus.NR && TotalMatches == 0)
                    return "NR";
                
                return Rating.ToString("F3"); // Show 2.500, 3.456, etc.
            }
        }

        [NotMapped]
        public decimal WinRate => TotalMatches > 0 ? (decimal)Wins / TotalMatches * 100 : 0;

        [NotMapped]
        public int DaysSinceLastMatch => LastMatchDate.HasValue 
            ? (DateTime.UtcNow - LastMatchDate.Value).Days 
            : int.MaxValue;
    }
}