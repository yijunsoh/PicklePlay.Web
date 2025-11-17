using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    /// <summary>
    /// Represents a match that has been processed for RANK ratings
    /// </summary>
    public class RankMatch
    {
        [Key]
        public int RankMatchId { get; set; } // ⬅️ ADD: Primary key named MatchId alternative

        [Required]
        public int ScheduleId { get; set; }

        [ForeignKey("ScheduleId")]
        public Schedule? Schedule { get; set; }

        [Required]
        public MatchFormat Format { get; set; }

        // Team 1 Players
        [Required]
        public int Team1Player1Id { get; set; }

        [ForeignKey("Team1Player1Id")]
        public User? Team1Player1 { get; set; }

        public int? Team1Player2Id { get; set; }

        [ForeignKey("Team1Player2Id")]
        public User? Team1Player2 { get; set; }

        // Team 2 Players
        [Required]
        public int Team2Player1Id { get; set; }

        [ForeignKey("Team2Player1Id")]
        public User? Team2Player1 { get; set; }

        public int? Team2Player2Id { get; set; }

        [ForeignKey("Team2Player2Id")]
        public User? Team2Player2 { get; set; }

        // Match Result
        [Required]
        public int Team1Score { get; set; }

        [Required]
        public int Team2Score { get; set; }

        // ⬇️ ADD: Rating tracking properties
        [Required]
        [Column(TypeName = "decimal(6,3)")]
        public decimal Team1RatingBefore { get; set; }

        [Required]
        [Column(TypeName = "decimal(6,3)")]
        public decimal Team2RatingBefore { get; set; }

        [Required]
        [Column(TypeName = "decimal(6,3)")]
        public decimal Team1RatingChange { get; set; }

        [Required]
        [Column(TypeName = "decimal(6,3)")]
        public decimal Team2RatingChange { get; set; }

        // Timestamps
        [Required]
        public DateTime MatchDate { get; set; }

        [Required]
        public DateTime ProcessedAt { get; set; }

        // ⬇️ ADD: Convenience property for accessing MatchId
        [NotMapped]
        public int MatchId => RankMatchId;
    }
}