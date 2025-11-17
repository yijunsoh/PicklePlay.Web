using PicklePlay.Models;
using System;
using System.Collections.Generic;

namespace PicklePlay.Models.ViewModels
{
    public class PlayerMatchHistoryViewModel
    {
        public int UserId { get; set; }
        public string ?Username { get; set; }
        public string? ProfilePicture { get; set; }
        public string ?CurrentRating { get; set; }
        public string ?CurrentStatus { get; set; }
        public decimal ReliabilityScore { get; set; }
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public decimal WinRate { get; set; }
        public List<RankMatchHistoryViewModel> MatchHistory { get; set; } = new();
        public bool IsOwnProfile { get; set; }

            public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int PageSize { get; set; } = 5;
    }

    public class RankMatchHistoryViewModel
    {
        public int MatchId { get; set; }
        public DateTime MatchDate { get; set; }
        public MatchFormat Format { get; set; }
        public GameOutcome Outcome { get; set; }
        public decimal RatingBefore { get; set; }
        public decimal RatingAfter { get; set; }
        public decimal RatingChange { get; set; }
        public decimal ReliabilityBefore { get; set; }
        public decimal ReliabilityAfter { get; set; }
        public decimal KFactorUsed { get; set; }
        public int? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public List<string> OpponentNames { get; set; } = new();
        public int TeamScore { get; set; }
        public int OpponentScore { get; set; }

        public string CompetitionTitle { get; set; } = string.Empty;
    public int CompetitionId { get; set; }

        public string FormattedDate => MatchDate.ToString("MMM dd, yyyy");
        public string FormattedTime => MatchDate.ToString("h:mm tt");
        public string ScoreDisplay => $"{TeamScore} - {OpponentScore}";
        public bool IsWin => Outcome == GameOutcome.Win;
        public string OutcomeDisplay => IsWin ? "Win" : "Loss";
        public string OutcomeBadge => IsWin ? "success" : "danger";
    }

    public class LeaderboardViewModel
    {
        public List<LeaderboardPlayerViewModel> Players { get; set; } = new();
        public string CurrentFilter { get; set; } = "All";
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int TotalPlayers { get; set; }
    }

    public class LeaderboardPlayerViewModel
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string ?Username { get; set; }
        public string? ProfilePicture { get; set; }
        public string ?Rating { get; set; }
        public decimal NumericRating { get; set; }
        public string ?Status { get; set; }
        public decimal ReliabilityScore { get; set; }
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public decimal WinRate { get; set; }
        public DateTime? LastMatchDate { get; set; }
        public string? Location { get; set; }

        public string StatusBadge => Status switch
        {
            "Reliable" => "success",
            "Provisional" => "warning",
            _ => "secondary"
        };

        public string LastPlayedDisplay => LastMatchDate.HasValue
            ? $"{(DateTime.UtcNow - LastMatchDate.Value).TotalDays:F0} days ago"
            : "Never";
    }

   
    public class MatchSubmissionRequest
    {
        public int ScheduleId { get; set; }
        public MatchFormat Format { get; set; }
        public int Team1Player1Id { get; set; }
        public int? Team1Player2Id { get; set; }
        public int Team2Player1Id { get; set; }
        public int? Team2Player2Id { get; set; }
        public int Team1Score { get; set; }
        public int Team2Score { get; set; }
    }
}