using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Services
{
    public class RankAlgorithmService
    {
        private readonly ApplicationDbContext _context;

        // Constants
        private const decimal DEFAULT_RATING = 2.500m;
        private const int TARGET_GAME_SCORE = 11;
        private const int MIN_MATCHES_FOR_PROVISIONAL = 5;
        private const int MIN_MATCHES_FOR_RELIABLE = 10;
        private const decimal MIN_RELIABILITY_FOR_RELIABLE = 80m;

        public RankAlgorithmService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get or create a player's rank
        /// </summary>
        public async Task<PlayerRank> GetOrCreatePlayerRank(int userId)
        {
            var rank = await _context.PlayerRanks
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (rank == null)
            {
                rank = new PlayerRank
                {
                    UserId = userId,
                    Rating = DEFAULT_RATING,
                    ReliabilityScore = 0,
                    Status = RankStatus.NR,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                _context.PlayerRanks.Add(rank);
                await _context.SaveChangesAsync();

                Console.WriteLine($"✅ Created PlayerRank for User {userId} with rating {rank.Rating}");
            }
            else if (rank.Rating == 0m)
            {
                Console.WriteLine($"⚠️ Warning: User {userId} had rating 0.000, resetting to {DEFAULT_RATING}");
                rank.Rating = DEFAULT_RATING;
                _context.PlayerRanks.Update(rank);
                await _context.SaveChangesAsync();
            }

            return rank;
        }

        /// <summary>
        /// Calculate rating change based on match result
        /// Uses normalized win margin to prevent rating inflation
        /// </summary>
        public decimal CalculateRatingChange(
            decimal teamRating,
            decimal opponentRating,
            int teamScore,
            int opponentScore,
            decimal kFactor,
            int targetScore = TARGET_GAME_SCORE)
        {
            Console.WriteLine($"\n=== Rating Change Calculation ===");
            Console.WriteLine($"Team Rating: {teamRating}");
            Console.WriteLine($"Opponent Rating: {opponentRating}");
            Console.WriteLine($"Score: {teamScore} - {opponentScore}");
            Console.WriteLine($"K-Factor: {kFactor}");

            // Treat NR opponents as default rating
            decimal effectiveOpponentRating = opponentRating == 0m ? DEFAULT_RATING : opponentRating;

            // Calculate expected win probability (0.0 to 1.0)
            decimal ratingDiff = effectiveOpponentRating - teamRating;
            double exponent = (double)ratingDiff / 2.0;
            double expectedProbability = 1.0 / (1.0 + Math.Pow(10, exponent));

            Console.WriteLine($"Expected Win Probability: {expectedProbability:P1}");

            // Determine actual result (1.0 for win, 0.0 for loss)
            decimal actualResult = teamScore > opponentScore ? 1.0m : 0.0m;

            // Calculate normalized win margin (0.0 to 1.0)
            int totalPoints = teamScore + opponentScore;
            decimal winMargin = totalPoints > 0 
                ? Math.Abs(teamScore - opponentScore) / (decimal)totalPoints 
                : 0m;

            Console.WriteLine($"Win Margin: {winMargin:P1}");

            // Margin multiplier (1.0 to 1.5)
            // Close games: multiplier = 1.0
            // Blowouts: multiplier = 1.3-1.5
            decimal marginMultiplier = 1.0m + (winMargin * 0.5m);
            marginMultiplier = Math.Min(marginMultiplier, 1.5m);

            Console.WriteLine($"Margin Multiplier: {marginMultiplier:F2}");

            // Core formula: Rating change based on expectation vs reality
            decimal performanceDiff = actualResult - (decimal)expectedProbability;
            decimal baseChange = performanceDiff * kFactor;
            decimal finalChange = baseChange * marginMultiplier;

            Console.WriteLine($"Performance Difference: {performanceDiff:F3}");
            Console.WriteLine($"Base Change: {baseChange:F3}");
            Console.WriteLine($"Final Change (with margin): {finalChange:F3}");
            Console.WriteLine($"=================================\n");

            return Math.Round(finalChange, 3);
        }

        /// <summary>
        /// Calculate K-factor based on reliability score
        /// </summary>
        public decimal CalculateKFactor(decimal reliabilityScore)
        {
            if (reliabilityScore >= 80)
                return 0.20m; // Reliable players
            else if (reliabilityScore >= 60)
                return 0.30m; // Provisional players
            else if (reliabilityScore >= 40)
                return 0.40m; // Developing players
            else
                return 0.50m; // New players
        }

        /// <summary>
        /// Calculate reliability score (0-100)
        /// </summary>
        public async Task<decimal> CalculateReliabilityScore(int userId)
        {
            var matches = await _context.RankMatchHistories
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.MatchDate)
                .ToListAsync();

            if (!matches.Any())
                return 0m;

            decimal score = 0m;

            // 1. Match Frequency (40 points)
            int totalMatches = matches.Count;
            if (totalMatches >= 50)
                score += 40m;
            else if (totalMatches >= 30)
                score += 35m;
            else if (totalMatches >= 20)
                score += 30m;
            else if (totalMatches >= 10)
                score += 25m;
            else if (totalMatches >= 5)
                score += 15m;
            else
                score += 5m;

            // 2. Recency (20 points)
            var lastMatch = matches.First().MatchDate;
            var daysSinceLastMatch = (DateTime.UtcNow - lastMatch).TotalDays;

            if (daysSinceLastMatch <= 7)
                score += 20m;
            else if (daysSinceLastMatch <= 14)
                score += 18m;
            else if (daysSinceLastMatch <= 30)
                score += 15m;
            else if (daysSinceLastMatch <= 60)
                score += 10m;
            else if (daysSinceLastMatch <= 90)
                score += 5m;

            // 3. Partner Diversity (20 points)
            var uniquePartners = matches
                .Where(m => m.PartnerId.HasValue)
                .Select(m => m.PartnerId!.Value)
                .Distinct()
                .Count();

            if (uniquePartners >= 10)
                score += 20m;
            else if (uniquePartners >= 7)
                score += 15m;
            else if (uniquePartners >= 5)
                score += 12m;
            else if (uniquePartners >= 3)
                score += 8m;
            else if (uniquePartners >= 1)
                score += 4m;

            // 4. Opponent Diversity (15 points)
            var uniqueOpponents = await GetUniqueOpponentCount(userId);

            if (uniqueOpponents >= 20)
                score += 15m;
            else if (uniqueOpponents >= 15)
                score += 12m;
            else if (uniqueOpponents >= 10)
                score += 10m;
            else if (uniqueOpponents >= 5)
                score += 6m;
            else if (uniqueOpponents >= 2)
                score += 3m;

            // 5. Community Diversity (5 points)
            var uniqueCommunities = await GetUniqueCommunityCount(userId);

            if (uniqueCommunities >= 5)
                score += 5m;
            else if (uniqueCommunities >= 3)
                score += 4m;
            else if (uniqueCommunities >= 2)
                score += 3m;
            else if (uniqueCommunities >= 1)
                score += 2m;

            return Math.Min(score, 100m);
        }

        /// <summary>
        /// Determine rank status based on matches and reliability
        /// </summary>
        public RankStatus DetermineRankStatus(int totalMatches, decimal reliabilityScore)
        {
            if (totalMatches == 0)
                return RankStatus.NR;

            if (totalMatches >= MIN_MATCHES_FOR_RELIABLE && reliabilityScore >= MIN_RELIABILITY_FOR_RELIABLE)
                return RankStatus.Reliable;

            if (totalMatches >= MIN_MATCHES_FOR_PROVISIONAL)
                return RankStatus.Provisional;

            return RankStatus.NR;
        }

        /// <summary>
        /// Update network diversity tracking
        /// </summary>
        public async Task UpdateNetworkDiversity(int userId)
        {
            var rank = await _context.PlayerRanks
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (rank == null) return;

            // Count unique partners
            rank.UniquePartners = await _context.RankMatchHistories
                .Where(h => h.UserId == userId && h.PartnerId.HasValue)
                .Select(h => h.PartnerId!.Value)
                .Distinct()
                .CountAsync();

            // Count unique opponents
            rank.UniqueOpponents = await GetUniqueOpponentCount(userId);

            // Count unique communities
            rank.UniqueCommunities = await GetUniqueCommunityCount(userId);

            _context.PlayerRanks.Update(rank);
        }

        private async Task<int> GetUniqueOpponentCount(int userId)
        {
            var userMatches = await _context.RankMatchHistories
                .Where(h => h.UserId == userId)
                .Select(h => h.RankMatchId)
                .Distinct()
                .ToListAsync();

            var opponentIds = await _context.RankMatchHistories
                .Where(h => userMatches.Contains(h.RankMatchId) && h.UserId != userId)
                .Select(h => h.UserId)
                .Distinct()
                .ToListAsync();

            return opponentIds.Count;
        }

        private async Task<int> GetUniqueCommunityCount(int userId)
        {
            var scheduleIds = await _context.RankMatches
                .Where(rm => rm.Team1Player1Id == userId ||
                            rm.Team1Player2Id == userId ||
                            rm.Team2Player1Id == userId ||
                            rm.Team2Player2Id == userId)
                .Select(rm => rm.ScheduleId)
                .Distinct()
                .ToListAsync();

            var communityIds = await _context.Schedules
                .Where(s => scheduleIds.Contains(s.ScheduleId) && s.CommunityId.HasValue)
                .Select(s => s.CommunityId!.Value)
                .Distinct()
                .ToListAsync();

            return Math.Max(1, communityIds.Count);
        }
    }
}