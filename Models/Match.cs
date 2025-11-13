using PicklePlay.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class Match
    {
        [Key]
        public int MatchId { get; set; }

        [Required]
        [ForeignKey("Schedule")]
        public int ScheduleId { get; set; }
        public virtual Schedule? Schedule { get; set; }

        [ForeignKey("Team1")]
        public int? Team1Id { get; set; }
        public virtual Team? Team1 { get; set; }

        [ForeignKey("Team2")]
        public int? Team2Id { get; set; }
        public virtual Team? Team2 { get; set; }

        public string? Team1Score { get; set; } // e.g., "11, 8, 11"
        public string? Team2Score { get; set; } // e.g., "9, 11, 5"

        public DateTime? MatchTime { get; set; }
        public string? Court { get; set; }

        public MatchStatus Status { get; set; } = MatchStatus.Active;

        public string? RoundName { get; set; } // e.g., "Pool A", "Quarter-Finals"
        public int RoundNumber { get; set; } = 1; // 1 = First Round, 2 = Second, etc.
        public int MatchNumber { get; set; } // Order within the round

        [ForeignKey("Winner")]
        public int? WinnerId { get; set; }
        public virtual Team? Winner { get; set; }

        public bool IsBye { get; set; } = false;

        [ForeignKey("LastUpdatedByUser")]
        public int? LastUpdatedByUserId { get; set; }
        public virtual User? LastUpdatedByUser { get; set; }

        // The MatchId that the winner of this match advances to
        public int? NextMatchId { get; set; }

        // The MatchId that the loser of this match advances to (for 3rd place match)
        public int? NextLoserMatchId { get; set; }

        // Tracks which slot the winner/loser takes in the next match (1 or 2)
        public int? MatchPosition { get; set; }

        // *** ADD THIS PROPERTY ***
    public bool IsThirdPlaceMatch { get; set; }
    }

    public enum MatchStatus
    {
        Active,      // Upcoming, not yet started (Green)
        Progressing, // Live, in-play (Yellow)
        Done,        // Finished (Grey)
        Bye          // For BYE rounds (Grey)
    }
}