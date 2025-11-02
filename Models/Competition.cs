using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PicklePlay.Models
{
    [Table("competition")]
    public class Competition
    {
        [Key]
        [ForeignKey("Schedule")] // Links this entity to the Schedule property below
        [Column("schedule_id")] // Maps to the DB column
        public int ScheduleId { get; set; }

        // --- Competition Specific Properties ---
        [Column("format")]
        public CompetitionFormat Format { get; set; } = CompetitionFormat.PoolPlay;

        [Column("numPool")]
        public int NumPool { get; set; } = 4;

        [Column("winnersPerPool")]
        public int WinnersPerPool { get; set; } = 1;

        [Column("thirdPlaceMatch")]
        public bool ThirdPlaceMatch { get; set; } = true; // Assuming 0=Yes maps to true

        [Column("doublePool")]
        public bool DoublePool { get; set; } = false; // Assuming 1=No maps to false

        [Column("standingCalculation")]
        public StandingCalculation StandingCalculation { get; set; } = StandingCalculation.WinLossPoints;

        // Points (only relevant if StandingCalculation is WinLossPoints)
        [Column("standardWin")]
        public int StandardWin { get; set; } = 3;
        [Column("standardLoss")]
        public int StandardLoss { get; set; } = 0;
        [Column("tieBreakWin")]
        public int TieBreakWin { get; set; } = 3;
        [Column("tieBreakLoss")]
        public int TieBreakLoss { get; set; } = 1;
        [Column("draw")]
        public int Draw { get; set; } = 1;

        [Column("matchRule")]
        [StringLength(255)]
        public string? MatchRule { get; set; }

        // --- Navigation Property ---
        // Represents the relationship back to the Schedule entity
        public virtual Schedule Schedule { get; set; } = null!; // Required relationship
    }

    // --- Enums for Competition ---
    // (You'll need to create these based on your comments)
    public enum CompetitionFormat : byte // Use byte for TINYINT
    {
        PoolPlay = 0,
        Elimination = 1,
        RoundRobin = 2
    }
    
    public enum StandingCalculation : byte
    {
        WinLossPoints = 0,
        WinPercent = 1,
        GamesWinPercent = 2,
        GamesWon = 3,
        TotalScores = 4
    }
}