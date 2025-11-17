using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    public class RankController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly RankAlgorithmService _rankAlgorithm;
        private readonly RankMatchProcessingService _matchProcessing;

        public RankController(
            ApplicationDbContext context,
            RankAlgorithmService rankAlgorithm,
            RankMatchProcessingService matchProcessing)
        {
            _context = context;
            _rankAlgorithm = rankAlgorithm;
            _matchProcessing = matchProcessing;
        }

        private int? GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId");
        }

        /// <summary>
        /// Get player rank info for profile display
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPlayerRank(int userId)
        {
            try
            {
                // ⬇️ ADD: Set no-cache headers
                Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");

                var rank = await _context.PlayerRanks
                    .FirstOrDefaultAsync(r => r.UserId == userId);

                if (rank == null || rank.TotalMatches == 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "No RANK data available"
                    });
                }

                return Ok(new
                {
                    success = true,
                    rank = new
                    {
                        userId = rank.UserId,
                        rating = rank.DisplayRating,
                        status = rank.Status.ToString(),
                        reliabilityScore = rank.ReliabilityScore,
                        totalMatches = rank.TotalMatches,
                        wins = rank.Wins,
                        losses = rank.Losses,
                        winRate = rank.WinRate,
                        singlesMatches = rank.SinglesMatches,
                        doublesMatches = rank.DoublesMatches,
                        uniquePartners = rank.UniquePartners,
                        uniqueOpponents = rank.UniqueOpponents,
                        lastMatchDate = rank.LastMatchDate,
                        lastUpdated = rank.LastUpdated
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting player rank: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving RANK data",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get player's match history
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMatchHistory(int userId, int skip = 0, int take = 20)
        {
            try
            {
                var history = await _context.RankMatchHistories
                    .Include(h => h.RankMatch)
                    .Include(h => h.Partner)
                    .Where(h => h.UserId == userId)
                    .OrderByDescending(h => h.MatchDate)
                    .Skip(skip)
                    .Take(take)
                    .Select(h => new
                    {
                        historyId = h.HistoryId,
                        matchDate = h.MatchDate,
                        outcome = h.Outcome.ToString(),
                        format = h.Format.ToString(),
                        partnerName = h.Partner != null ? h.Partner.Username : null,
                        ratingBefore = h.RatingBefore,
                        ratingAfter = h.RatingAfter,
                        ratingChange = h.RatingChange,
                        reliabilityBefore = h.ReliabilityBefore,
                        reliabilityAfter = h.ReliabilityAfter,
                        kFactorUsed = h.KFactorUsed
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    history = history,
                    hasMore = history.Count == take
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting match history: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error retrieving match history" });
            }
        }

        /// <summary>
        /// Submit match results to RANK system
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMatchResults([FromForm] MatchSubmissionRequest request)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Unauthorized(new { success = false, message = "User not authenticated" });
            }

            try
            {
                // Validate schedule exists and is completed
                var schedule = await _context.Schedules
                    .Include(s => s.Participants)
                    .FirstOrDefaultAsync(s => s.ScheduleId == request.ScheduleId);

                if (schedule == null)
                {
                    return NotFound(new { success = false, message = "Schedule not found" });
                }

                if (schedule.Status != ScheduleStatus.Completed)
                {
                    return BadRequest(new { success = false, message = "Only completed competitions can be submitted to RANK" });
                }

                // Check if user is organizer
                bool isOrganizer = schedule.Participants.Any(p =>
                    p.UserId == currentUserId.Value &&
                    p.Role == ParticipantRole.Organizer);

                if (!isOrganizer)
                {
                    return Forbid();
                }

                // Check if already submitted
                var existingSubmission = await _context.RankMatches
                    .AnyAsync(rm => rm.ScheduleId == request.ScheduleId);

                if (existingSubmission)
                {
                    return BadRequest(new { success = false, message = "This competition has already been submitted to RANK" });
                }

                // Process each match
                var matchData = new MatchSubmissionData
                {
                    Format = request.Format,
                    Team1Player1Id = request.Team1Player1Id,
                    Team1Player2Id = request.Team1Player2Id,
                    Team2Player1Id = request.Team2Player1Id,
                    Team2Player2Id = request.Team2Player2Id,
                    Team1Score = request.Team1Score,
                    Team2Score = request.Team2Score,
                    MatchDate = schedule.EndTime ?? DateTime.UtcNow
                };

                bool success = await _matchProcessing.ProcessMatch(request.ScheduleId, matchData);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Match results successfully submitted to RANK system"
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Failed to process match results"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error submitting match results: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while submitting match results"
                });
            }
        }

        /// <summary>
        /// Get leaderboard
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLeaderboard(int skip = 0, int take = 50, string filter = "all")
        {
            try
            {
                var query = _context.PlayerRanks
                    .Include(r => r.User)
                    .Where(r => r.Status != RankStatus.NR);

                // Apply filters
                if (filter == "reliable")
                {
                    query = query.Where(r => r.Status == RankStatus.Reliable);
                }
                else if (filter == "singles")
                {
                    query = query.Where(r => r.SinglesMatches > r.DoublesMatches);
                }
                else if (filter == "doubles")
                {
                    query = query.Where(r => r.DoublesMatches > r.SinglesMatches);
                }

                var leaderboard = await query
                    .OrderByDescending(r => r.Rating)
                    .Skip(skip)
                    .Take(take)
                    .Select((r, index) => new
                    {
                        rank = skip + index + 1,
                        userId = r.UserId,
                        username = r.User!.Username,
                        profilePicture = r.User.ProfilePicture,
                        rating = r.DisplayRating,
                        numericRating = r.Rating,
                        reliabilityScore = r.ReliabilityScore,
                        status = r.Status.ToString(),
                        totalMatches = r.TotalMatches,
                        winRate = r.WinRate
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    leaderboard = leaderboard,
                    hasMore = leaderboard.Count == take
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting leaderboard: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error retrieving leaderboard" });
            }
        }

        /// <summary>
        /// Auto-submit all completed matches from a competition to RANK
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoSubmitCompetitionResults(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Unauthorized(new { success = false, message = "User not authenticated" });
            }

            try
            {
                // Validate schedule exists and is completed
                var schedule = await _context.Schedules
                    .Include(s => s.Participants)
                    .Include(s => s.Competition)
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return NotFound(new { success = false, message = "Competition not found" });
                }

                if (schedule.Status != ScheduleStatus.Completed)
                {
                    return BadRequest(new { success = false, message = "Only completed competitions can be submitted to RANK" });
                }

                // Check if user is organizer
                bool isOrganizer = schedule.Participants.Any(p =>
                    p.UserId == currentUserId.Value &&
                    p.Role == ParticipantRole.Organizer);

                if (!isOrganizer)
                {
                    return Forbid();
                }

                // Check if already submitted
                var existingSubmission = await _context.RankMatches
                    .AnyAsync(rm => rm.ScheduleId == scheduleId);

                if (existingSubmission)
                {
                    return BadRequest(new { success = false, message = "This competition has already been submitted to RANK" });
                }

                // Get all completed matches
                var completedMatches = await _context.Matches
                    .Include(m => m.Team1)
                        .ThenInclude(t => t!.TeamMembers)
                            .ThenInclude(tm => tm.User)
                    .Include(m => m.Team2)
                        .ThenInclude(t => t!.TeamMembers)
                            .ThenInclude(tm => tm.User)
                    .Where(m => m.ScheduleId == scheduleId &&
                               m.Status == MatchStatus.Done &&
                               m.Team1Id.HasValue &&
                               m.Team2Id.HasValue)
                    .ToListAsync();

                if (!completedMatches.Any())
                {
                    return BadRequest(new { success = false, message = "No completed matches found in this competition" });
                }

                int processedCount = 0;
                int skippedCount = 0;
                var errors = new List<string>();

                foreach (var match in completedMatches)
                {
                    try
                    {
                        // Determine match format based on team size
                        var team1Members = match.Team1!.TeamMembers
                            .Where(tm => tm.Status == TeamMemberStatus.Joined)
                            .ToList();
                        var team2Members = match.Team2!.TeamMembers
                            .Where(tm => tm.Status == TeamMemberStatus.Joined)
                            .ToList();

                        // Skip if teams don't have valid members
                        if (!team1Members.Any() || !team2Members.Any())
                        {
                            skippedCount++;
                            errors.Add($"Match {match.MatchId}: Invalid team composition");
                            continue;
                        }

                        var format = (team1Members.Count == 1 && team2Members.Count == 1)
                            ? MatchFormat.Singles
                            : MatchFormat.Doubles;

                        // Parse scores to get final score
                        var (team1FinalScore, team2FinalScore) = ParseMatchScore(match.Team1Score!, match.Team2Score!);

                        if (team1FinalScore < 0 || team2FinalScore < 0)
                        {
                            skippedCount++;
                            errors.Add($"Match {match.MatchId}: Invalid score format");
                            continue;
                        }

                        // Create match submission data
                        var matchData = new MatchSubmissionData
                        {
                            Format = format,
                            Team1Player1Id = team1Members[0].UserId,
                            Team1Player2Id = format == MatchFormat.Doubles && team1Members.Count > 1
                                ? team1Members[1].UserId
                                : (int?)null,
                            Team2Player1Id = team2Members[0].UserId,
                            Team2Player2Id = format == MatchFormat.Doubles && team2Members.Count > 1
                                ? team2Members[1].UserId
                                : (int?)null,
                            Team1Score = team1FinalScore,
                            Team2Score = team2FinalScore,
                            MatchDate = match.MatchTime ?? schedule.EndTime ?? DateTime.UtcNow
                        };

                        // Process the match
                        bool success = await _matchProcessing.ProcessMatch(scheduleId, matchData);

                        if (success)
                        {
                            processedCount++;
                        }
                        else
                        {
                            skippedCount++;
                            errors.Add($"Match {match.MatchId}: Processing failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        errors.Add($"Match {match.MatchId}: {ex.Message}");
                        Console.WriteLine($"Error processing match {match.MatchId}: {ex.Message}");
                    }
                }

                var resultMessage = $"Successfully processed {processedCount} matches";
                if (skippedCount > 0)
                {
                    resultMessage += $", skipped {skippedCount} matches";
                }

                return Ok(new
                {
                    success = true,
                    message = resultMessage,
                    details = new
                    {
                        totalMatches = completedMatches.Count,
                        processed = processedCount,
                        skipped = skippedCount,
                        errors = errors
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error auto-submitting competition results: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while processing competition results",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get summary of completed matches for RANK submission preview
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCompetitionMatchSummary(int scheduleId)
        {
            try
            {
                var completedMatches = await _context.Matches
                    .Include(m => m.Team1)
                        .ThenInclude(t => t!.TeamMembers)
                    .Include(m => m.Team2)
                        .ThenInclude(t => t!.TeamMembers)
                    .Where(m => m.ScheduleId == scheduleId && 
                               m.Status == MatchStatus.Done &&
                               m.Team1Id.HasValue && 
                               m.Team2Id.HasValue)
                    .ToListAsync();

                int singlesCount = 0;
                int doublesCount = 0;

                foreach (var match in completedMatches)
                {
                    var team1Members = match.Team1!.TeamMembers
                        .Count(tm => tm.Status == TeamMemberStatus.Joined);
                    var team2Members = match.Team2!.TeamMembers
                        .Count(tm => tm.Status == TeamMemberStatus.Joined);

                    if (team1Members == 1 && team2Members == 1)
                        singlesCount++;
                    else if (team1Members >= 2 && team2Members >= 2)
                        doublesCount++;
                }

                return Ok(new
                {
                    success = true,
                    summary = new
                    {
                        totalMatches = completedMatches.Count,
                        singlesMatches = singlesCount,
                        doublesMatches = doublesCount,
                        validMatches = singlesCount + doublesCount
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting match summary: {ex.Message}");
                return Ok(new
                {
                    success = false,
                    message = "Failed to load match summary"
                });
            }
        }

        /// <summary>
        /// Helper method to parse match scores and return final scores
        /// </summary>
        private (int team1Score, int team2Score) ParseMatchScore(string team1ScoreStr, string team2ScoreStr)
        {
            try
            {
                // ⬇️ ADD: Debug logging
                Console.WriteLine($"Parsing scores: Team1='{team1ScoreStr}', Team2='{team2ScoreStr}'");
                
                if (string.IsNullOrWhiteSpace(team1ScoreStr) || string.IsNullOrWhiteSpace(team2ScoreStr))
                {
                    Console.WriteLine("Error: Empty score strings");
                    return (-1, -1);
                }

                // Parse comma-separated scores (e.g., "21,19,15")
                var team1Scores = team1ScoreStr.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out int score) ? score : 0)
                    .ToList();

                var team2Scores = team2ScoreStr.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out int score) ? score : 0)
                    .ToList();

                if (team1Scores.Count != team2Scores.Count)
                {
                    Console.WriteLine($"Error: Score count mismatch - Team1: {team1Scores.Count}, Team2: {team2Scores.Count}");
                    return (-1, -1);
                }

                // ⬇️ OPTION 1: Use last set score only (final game)
                int team1Final = team1Scores.Last();
                int team2Final = team2Scores.Last();
                
                // ⬇️ OPTION 2: Use total across all sets
                // int team1Final = team1Scores.Sum();
                // int team2Final = team2Scores.Sum();

                Console.WriteLine($"Parsed final scores: Team1={team1Final}, Team2={team2Final}");
                
                return (team1Final, team2Final);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing scores: {ex.Message}");
                return (-1, -1);
            }
        }

        /// <summary>
        /// Check if competition has been submitted to RANK
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckCompetitionRankStatus(int scheduleId)
        {
            try
            {
                var hasSubmission = await _context.RankMatches
                    .AnyAsync(rm => rm.ScheduleId == scheduleId);

                if (hasSubmission)
                {
                    // Get submission details
                    var submissionDate = await _context.RankMatches
                        .Where(rm => rm.ScheduleId == scheduleId)
                        .Select(rm => rm.ProcessedAt)
                        .FirstOrDefaultAsync();

                    var matchesProcessed = await _context.RankMatches
                        .CountAsync(rm => rm.ScheduleId == scheduleId);

                    return Ok(new
                    {
                        success = true,
                        isSubmitted = true,
                        submissionDate = submissionDate,
                        matchesProcessed = matchesProcessed
                    });
                }

                return Ok(new
                {
                    success = true,
                    isSubmitted = false
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking RANK status: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error checking submission status"
                });
            }
        }

        /// <summary>
        /// Reset all RANK data for a competition (for testing/fixing inflation)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetAllRankData(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            try
            {
                // Verify user is organizer
                var schedule = await _context.Schedules
                    .Include(s => s.Participants)
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return Json(new { success = false, message = "Competition not found" });
                }

                bool isOrganizer = schedule.Participants.Any(p =>
                    p.UserId == currentUserId.Value &&
                    p.Role == ParticipantRole.Organizer);

                if (!isOrganizer)
                {
                    return Json(new { success = false, message = "Only organizers can reset RANK data" });
                }

                // Delete all rank data for this competition
                var rankMatches = await _context.RankMatches
                    .Where(rm => rm.ScheduleId == scheduleId)
                    .ToListAsync();

                if (!rankMatches.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "No RANK data found for this competition"
                    });
                }

                var matchIds = rankMatches.Select(rm => rm.RankMatchId).ToList();

                var histories = await _context.RankMatchHistories
                    .Where(h => matchIds.Contains(h.RankMatchId))
                    .ToListAsync();

                // Get affected players
                var affectedPlayerIds = histories.Select(h => h.UserId).Distinct().ToList();

                // Remove histories and matches
                _context.RankMatchHistories.RemoveRange(histories);
                _context.RankMatches.RemoveRange(rankMatches);
                await _context.SaveChangesAsync();

                // Recalculate all affected player ranks from scratch
                foreach (var playerId in affectedPlayerIds)
                {
                    await RecalculatePlayerRank(playerId);
                }

                return Json(new
                {
                    success = true,
                    message = $"Successfully reset RANK data for {affectedPlayerIds.Count} players",
                    affectedPlayers = affectedPlayerIds.Count,
                    matchesRemoved = rankMatches.Count
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resetting RANK data: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return Json(new
                {
                    success = false,
                    message = "An error occurred while resetting RANK data",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Recalculate a player's RANK rating from all their match history
        /// Useful for fixing rating issues or after algorithm changes
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalculatePlayerRank(int userId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            // Users can only recalculate their own rank (or admin can recalculate any)
            if (currentUserId.Value != userId && !User.IsInRole("Admin"))
            {
                return Json(new { success = false, message = "You can only recalculate your own rating" });
            }

            try
            {
                var playerRank = await _context.PlayerRanks
                    .FirstOrDefaultAsync(r => r.UserId == userId);

                if (playerRank == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No RANK data found. Play some matches first!"
                    });
                }

                decimal oldRating = playerRank.Rating;
                decimal oldReliability = playerRank.ReliabilityScore;

                // Get all match history for this player, ordered by date
                var matchHistory = await _context.RankMatchHistories
                    .Where(h => h.UserId == userId)
                    .OrderBy(h => h.MatchDate)
                    .ToListAsync();

                if (!matchHistory.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "No match history found to recalculate from"
                    });
                }

                // Reset to default and replay all matches
                playerRank.Rating = 2.500m; // Start from default
                playerRank.TotalMatches = 0;
                playerRank.Wins = 0;
                playerRank.Losses = 0;
                playerRank.SinglesMatches = 0;
                playerRank.DoublesMatches = 0;
                playerRank.LastMatchDate = null;

                // Replay each match in chronological order
                foreach (var history in matchHistory)
                {
                    // Get opponent's current rating at time of match
                    var opponentId = history.PartnerId ?? 0;
                    if (opponentId == 0)
                    {
                        // Find opponent from the match
                        var rankMatch = await _context.RankMatches
                            .FirstOrDefaultAsync(rm => rm.RankMatchId == history.RankMatchId);

                        if (rankMatch != null)
                        {
                            // Determine who the opponent is
                            if (rankMatch.Team1Player1Id == userId)
                                opponentId = rankMatch.Team2Player1Id;
                            else if (rankMatch.Team1Player2Id == userId)
                                opponentId = rankMatch.Team2Player1Id;
                            else if (rankMatch.Team2Player1Id == userId)
                                opponentId = rankMatch.Team1Player1Id;
                            else if (rankMatch.Team2Player2Id == userId)
                                opponentId = rankMatch.Team1Player1Id;
                        }
                    }

                    var opponentRank = await _context.PlayerRanks
                        .FirstOrDefaultAsync(r => r.UserId == opponentId);

                    decimal opponentRating = opponentRank?.Rating ?? 2.500m;

                    // Get match details
                    var rankMatch2 = await _context.RankMatches
                        .FirstOrDefaultAsync(rm => rm.RankMatchId == history.RankMatchId);

                    if (rankMatch2 == null) continue;

                    // Determine scores
                    int playerScore, opponentScore;
                    if (rankMatch2.Team1Player1Id == userId || rankMatch2.Team1Player2Id == userId)
                    {
                        playerScore = rankMatch2.Team1Score;
                        opponentScore = rankMatch2.Team2Score;
                    }
                    else
                    {
                        playerScore = rankMatch2.Team2Score;
                        opponentScore = rankMatch2.Team1Score;
                    }

                    // Calculate K-factor based on current reliability
                    decimal kFactor = _rankAlgorithm.CalculateKFactor(playerRank.ReliabilityScore);

                    // Calculate rating change using updated algorithm
                    decimal ratingChange = _rankAlgorithm.CalculateRatingChange(
                        playerRank.Rating,
                        opponentRating,
                        playerScore,
                        opponentScore,
                        kFactor
                    );

                    // Update rating
                    playerRank.Rating = Math.Max(0.5m, Math.Min(8.0m, playerRank.Rating + ratingChange));
                    playerRank.TotalMatches++;

                    if (history.Outcome == GameOutcome.Win)
                        playerRank.Wins++;
                    else
                        playerRank.Losses++;

                    if (history.Format == MatchFormat.Singles)
                        playerRank.SinglesMatches++;
                    else
                        playerRank.DoublesMatches++;

                    playerRank.LastMatchDate = history.MatchDate;

                    // Update history record with new calculated values
                    history.RatingBefore = playerRank.Rating - ratingChange;
                    history.RatingAfter = playerRank.Rating;
                    history.RatingChange = ratingChange;
                    history.KFactorUsed = kFactor;
                }

                // Recalculate network diversity
                await _rankAlgorithm.UpdateNetworkDiversity(userId);

                // Recalculate reliability score
                playerRank.ReliabilityScore = await _rankAlgorithm.CalculateReliabilityScore(userId);
                playerRank.Status = _rankAlgorithm.DetermineRankStatus(
                    playerRank.TotalMatches,
                    playerRank.ReliabilityScore
                );
                playerRank.LastUpdated = DateTime.UtcNow;

                // Save all changes
                _context.PlayerRanks.Update(playerRank);
                _context.RankMatchHistories.UpdateRange(matchHistory);
                await _context.SaveChangesAsync();

                decimal ratingDifference = playerRank.Rating - oldRating;
                decimal reliabilityDifference = playerRank.ReliabilityScore - oldReliability;

                return Json(new
                {
                    success = true,
                    message = "Rating successfully recalculated",
                    details = new
                    {
                        oldRating = oldRating.ToString("F3"),
                        newRating = playerRank.Rating.ToString("F3"),
                        ratingChange = ratingDifference.ToString("F3"),
                        oldReliability = oldReliability.ToString("F1"),
                        newReliability = playerRank.ReliabilityScore.ToString("F1"),
                        reliabilityChange = reliabilityDifference.ToString("F1"),
                        matchesProcessed = matchHistory.Count,
                        status = playerRank.Status.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error recalculating player rank: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return Json(new
                {
                    success = false,
                    message = "An error occurred while recalculating rating",
                    error = ex.Message
                });
            }
        }
    }

    // Request model for match submission
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