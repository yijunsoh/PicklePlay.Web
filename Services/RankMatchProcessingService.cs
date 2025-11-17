using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Services
{
    public class RankMatchProcessingService
    {
        private readonly ApplicationDbContext _context;
        private readonly RankAlgorithmService _rankAlgorithm;

        public RankMatchProcessingService(
            ApplicationDbContext context,
            RankAlgorithmService rankAlgorithm)
        {
            _context = context;
            _rankAlgorithm = rankAlgorithm;
        }

        /// <summary>
        /// Process a completed match and update rankings
        /// </summary>
        public async Task<bool> ProcessMatch(int scheduleId, MatchSubmissionData matchData)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get all player ranks (create if needed)
                var player1Rank = await _rankAlgorithm.GetOrCreatePlayerRank(matchData.Team1Player1Id);
                var player2Rank = await _rankAlgorithm.GetOrCreatePlayerRank(matchData.Team2Player1Id);

                PlayerRank? player1Partner = null;
                PlayerRank? player2Partner = null;

                if (matchData.Format == MatchFormat.Doubles)
                {
                    if (!matchData.Team1Player2Id.HasValue || !matchData.Team2Player2Id.HasValue)
                        throw new InvalidOperationException("Doubles match requires both partners");

                    player1Partner = await _rankAlgorithm.GetOrCreatePlayerRank(matchData.Team1Player2Id.Value);
                    player2Partner = await _rankAlgorithm.GetOrCreatePlayerRank(matchData.Team2Player2Id.Value);
                }

                // Calculate team average ratings
                decimal team1Rating = matchData.Format == MatchFormat.Singles
                    ? player1Rank.Rating
                    : (player1Rank.Rating + player1Partner!.Rating) / 2;

                decimal team2Rating = matchData.Format == MatchFormat.Singles
                    ? player2Rank.Rating
                    : (player2Rank.Rating + player2Partner!.Rating) / 2;

                Console.WriteLine($"\n=== Processing Match ===");
                Console.WriteLine($"Format: {matchData.Format}");
                Console.WriteLine($"Team 1 Rating: {team1Rating:F3}");
                Console.WriteLine($"Team 2 Rating: {team2Rating:F3}");
                Console.WriteLine($"Score: {matchData.Team1Score} - {matchData.Team2Score}");

                // ⬇️ UPDATED: Use new calculation method
                decimal kFactor1 = _rankAlgorithm.CalculateKFactor(player1Rank.ReliabilityScore);
                decimal kFactor2 = _rankAlgorithm.CalculateKFactor(player2Rank.ReliabilityScore);

                // Calculate rating changes for each team
                decimal team1Change = _rankAlgorithm.CalculateRatingChange(
                    team1Rating,
                    team2Rating,
                    matchData.Team1Score,
                    matchData.Team2Score,
                    kFactor1
                );

                decimal team2Change = _rankAlgorithm.CalculateRatingChange(
                    team2Rating,
                    team1Rating,
                    matchData.Team2Score,
                    matchData.Team1Score,
                    kFactor2
                );

                Console.WriteLine($"Team 1 Rating Change: {team1Change:F3}");
                Console.WriteLine($"Team 2 Rating Change: {team2Change:F3}");

                // Create match record
                var rankMatch = new RankMatch
                {
                    ScheduleId = scheduleId,
                    Format = matchData.Format,
                    Team1Player1Id = matchData.Team1Player1Id,
                    Team1Player2Id = matchData.Team1Player2Id,
                    Team2Player1Id = matchData.Team2Player1Id,
                    Team2Player2Id = matchData.Team2Player2Id,
                    Team1Score = matchData.Team1Score,
                    Team2Score = matchData.Team2Score,
                    Team1RatingBefore = team1Rating,
                    Team2RatingBefore = team2Rating,
                    Team1RatingChange = team1Change,
                    Team2RatingChange = team2Change,
                    MatchDate = matchData.MatchDate,
                    ProcessedAt = DateTime.UtcNow
                };

                _context.RankMatches.Add(rankMatch);
                await _context.SaveChangesAsync();

                // Update individual player ranks
                await UpdatePlayerRank(player1Rank, team1Change, matchData, true, matchData.Team1Player2Id, rankMatch.MatchId);
                await UpdatePlayerRank(player2Rank, team2Change, matchData, false, matchData.Team2Player2Id, rankMatch.MatchId);

                if (matchData.Format == MatchFormat.Doubles)
                {
                    await UpdatePlayerRank(player1Partner!, team1Change, matchData, true, matchData.Team1Player1Id, rankMatch.MatchId);
                    await UpdatePlayerRank(player2Partner!, team2Change, matchData, false, matchData.Team2Player1Id, rankMatch.MatchId);
                }

                await transaction.CommitAsync();
                Console.WriteLine("=== Match Processed Successfully ===\n");

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error processing match: {ex.Message}");
                return false;
            }
        }

        // Find the UpdatePlayerRank method and UPDATE the rating update section:

private async Task UpdatePlayerRank(
    PlayerRank rank,
    decimal ratingChange,
    MatchSubmissionData matchData,
    bool isTeam1,
    int? partnerId,
    int rankMatchId)
{
    // Debug logging
    Console.WriteLine($"=== Updating Player {rank.UserId} ===");
    Console.WriteLine($"Current Rating: {rank.Rating}");
    Console.WriteLine($"Rating Change: {ratingChange}");
    Console.WriteLine($"Reliability: {rank.ReliabilityScore}");
    
    // Store history
    var history = new RankMatchHistory
    {
        UserId = rank.UserId,
        RankMatchId = rankMatchId,
        Outcome = (isTeam1 && matchData.Team1Score > matchData.Team2Score) || 
                 (!isTeam1 && matchData.Team2Score > matchData.Team1Score) 
                 ? GameOutcome.Win 
                 : GameOutcome.Loss,
        Format = matchData.Format,
        PartnerId = partnerId,
        RatingBefore = rank.Rating,
        RatingAfter = rank.Rating + ratingChange,
        RatingChange = ratingChange,
        ReliabilityBefore = rank.ReliabilityScore,
        KFactorUsed = _rankAlgorithm.CalculateKFactor(rank.ReliabilityScore),
        MatchDate = matchData.MatchDate
    };

    // ⬇️ UPDATED: Apply rating change with safety caps
    decimal newRating = rank.Rating + ratingChange;
    
    // Apply safety caps
    newRating = Math.Max(0.5m, newRating);  // Minimum 0.5
    newRating = Math.Min(8.0m, newRating);  // Maximum 8.0
    newRating = Math.Round(newRating, 3);   // 3 decimal places
    
    Console.WriteLine($"New Rating (with caps): {newRating}");
    Console.WriteLine($"Match Outcome: {history.Outcome}");
    
    rank.Rating = newRating;
    rank.TotalMatches++;
    rank.LastMatchDate = matchData.MatchDate;

    if (matchData.Format == MatchFormat.Singles)
        rank.SinglesMatches++;
    else
        rank.DoublesMatches++;

    if (history.Outcome == GameOutcome.Win)
        rank.Wins++;
    else
        rank.Losses++;

    // Update network diversity
    await _rankAlgorithm.UpdateNetworkDiversity(rank.UserId);

    // Recalculate reliability
    rank.ReliabilityScore = await _rankAlgorithm.CalculateReliabilityScore(rank.UserId);
    rank.Status = _rankAlgorithm.DetermineRankStatus(rank.TotalMatches, rank.ReliabilityScore);
    rank.LastUpdated = DateTime.UtcNow;

    history.ReliabilityAfter = rank.ReliabilityScore;

    _context.RankMatchHistories.Add(history);
    _context.PlayerRanks.Update(rank);
    
    // Save and verify
    await _context.SaveChangesAsync();
    
    Console.WriteLine($"Final Rating After Save: {rank.Rating}");
    Console.WriteLine($"=== Update Complete ===\n");
}

        private bool ValidateMatchData(MatchSubmissionData matchData)
        {
            if (matchData.Team1Score < 0 || matchData.Team2Score < 0)
                return false;

            if (matchData.Team1Score == matchData.Team2Score)
                return false; // No ties

            if (matchData.Format == MatchFormat.Doubles)
            {
                if (!matchData.Team1Player2Id.HasValue || !matchData.Team2Player2Id.HasValue)
                    return false;
            }

            return true;
        }
    }

    // DTO for match submission
    public class MatchSubmissionData
    {
        public MatchFormat Format { get; set; }
        public int Team1Player1Id { get; set; }
        public int? Team1Player2Id { get; set; }
        public int Team2Player1Id { get; set; }
        public int? Team2Player2Id { get; set; }
        public int Team1Score { get; set; }
        public int Team2Score { get; set; }
        public DateTime MatchDate { get; set; }
    }
}