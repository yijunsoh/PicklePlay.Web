using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    public class RankMatchHistory
    {
        [Key]
        public int HistoryId { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [Required]
        public int RankMatchId { get; set; }

        [ForeignKey("RankMatchId")]
        public RankMatch? RankMatch { get; set; }

        // Player's perspective
        [Required]
        public GameOutcome Outcome { get; set; }

        [Required]
        public MatchFormat Format { get; set; }

        // Partner info (for doubles)
        public int? PartnerId { get; set; }

        [ForeignKey("PartnerId")]
        public User? Partner { get; set; }

        // Rating before and after
        [Column(TypeName = "decimal(5,3)")]
        public decimal RatingBefore { get; set; }

        [Column(TypeName = "decimal(5,3)")]
        public decimal RatingAfter { get; set; }

        [Column(TypeName = "decimal(6,3)")]
        public decimal RatingChange { get; set; }

        // Reliability before and after
        [Column(TypeName = "decimal(5,2)")]
        public decimal ReliabilityBefore { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ReliabilityAfter { get; set; }

        // K-factor used in this match
        [Column(TypeName = "decimal(5,3)")]
        public decimal KFactorUsed { get; set; }

        [Required]
        public DateTime MatchDate { get; set; }
    }
}