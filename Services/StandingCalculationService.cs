using PicklePlay.Data;
using PicklePlay.Models;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Application.Services
{
    public class StandingCalculationService
    {
        private readonly ApplicationDbContext _context;

        public StandingCalculationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<TeamStanding>> CalculatePoolStandings(int poolId, Competition competition)
        {
            var pool = await _context.Pools
                .Include(p => p.Teams)
                .FirstOrDefaultAsync(p => p.PoolId == poolId);

            if (pool == null || pool.Teams == null || !pool.Teams.Any())
                return new List<TeamStanding>();

            // Get all matches for this pool
            var matches = await _context.Matches
                .Where(m => m.RoundName == pool.PoolName && m.Status == MatchStatus.Done)
                .Include(m => m.Team1)
                .Include(m => m.Team2)
                .ToListAsync();

            var standings = new List<TeamStanding>();

            foreach (var team in pool.Teams)
            {
                var standing = new TeamStanding
                {
                    TeamId = team.TeamId,
                    TeamName = team.TeamName ?? string.Empty,
                    PoolName = pool.PoolName ?? string.Empty
                };

                // Get team's matches
                var teamMatches = matches.Where(m => 
                    m.Team1Id == team.TeamId || m.Team2Id == team.TeamId).ToList();

                foreach (var match in teamMatches)
                {
                    bool isTeam1 = match.Team1Id == team.TeamId;
                    var teamScore = isTeam1 ? match.Team1Score : match.Team2Score;
                    var opponentScore = isTeam1 ? match.Team2Score : match.Team1Score;

                    if (string.IsNullOrEmpty(teamScore) || string.IsNullOrEmpty(opponentScore))
                        continue;

                    var teamSets = ParseScores(teamScore);
                    var opponentSets = ParseScores(opponentScore);

                    int teamWins = 0, opponentWins = 0;
                    int teamTotalPoints = 0, opponentTotalPoints = 0;

                    for (int i = 0; i < Math.Min(teamSets.Length, opponentSets.Length); i++)
                    {
                        if (teamSets[i] > opponentSets[i]) teamWins++;
                        else if (opponentSets[i] > teamSets[i]) opponentWins++;

                        teamTotalPoints += teamSets[i];
                        opponentTotalPoints += opponentSets[i];
                    }

                    standing.GamesPlayed++;
                    standing.GamesWon += teamWins;
                    standing.GamesLost += opponentWins;
                    standing.TotalScore += teamTotalPoints;
                    
                    // Calculate score differential for this match
                    int matchScoreDifferential = teamTotalPoints - opponentTotalPoints;
                    standing.ScoreDifference += matchScoreDifferential;

                    // ⬇️ FIX: Determine if this is a tie-break based on SCORE DIFFERENTIAL
                    // Tie-break = close match (score differential ≤ 3 points)
                    bool isTieBreak = Math.Abs(matchScoreDifferential) <= 3;

                    // Match result
                    if (match.WinnerId == team.TeamId)
                    {
                        // Team WON this match
                        standing.MatchesWon++;
                        
                        if (competition.StandingCalculation == StandingCalculation.WinLossPoints)
                        {
                            if (isTieBreak)
                            {
                                // Tie-Break Win (close match)
                                standing.TieBreakWins++;
                                standing.Points += competition.TieBreakWin;
                            }
                            else
                            {
                                // Standard Win (dominant win)
                                standing.StandardWins++;
                                standing.Points += competition.StandardWin;
                            }
                        }
                    }
                    else if (match.WinnerId != null)
                    {
                        // Team LOST this match
                        standing.MatchesLost++;
                        
                        if (competition.StandingCalculation == StandingCalculation.WinLossPoints)
                        {
                            if (isTieBreak)
                            {
                                // Tie-Break Loss (close match)
                                standing.TieBreakLosses++;
                                standing.Points += competition.TieBreakLoss;
                            }
                            else
                            {
                                // Standard Loss (dominant loss)
                                standing.StandardLosses++;
                                standing.Points += competition.StandardLoss; // Usually 0 pts
                            }
                        }
                    }
                    else
                    {
                        // Draw (no winner)
                        standing.Draws++;
                        if (competition.StandingCalculation == StandingCalculation.WinLossPoints)
                        {
                            standing.Points += competition.Draw;
                        }
                    }

                    // Head-to-head tracking
                    int opponentId = isTeam1 ? match.Team2Id!.Value : match.Team1Id!.Value;
                    if (match.WinnerId == team.TeamId)
                    {
                        if (!standing.HeadToHeadWins.ContainsKey(opponentId))
                            standing.HeadToHeadWins[opponentId] = 0;
                        standing.HeadToHeadWins[opponentId]++;
                        
                        if (!standing.HeadToHeadDifference.ContainsKey(opponentId))
                            standing.HeadToHeadDifference[opponentId] = 0;
                        standing.HeadToHeadDifference[opponentId] += matchScoreDifferential;
                    }
                }

                // Calculate percentages
                if (standing.GamesPlayed > 0)
                {
                    standing.WinPercentage = (double)standing.MatchesWon / standing.GamesPlayed;
                    standing.GameWinPercentage = (double)standing.GamesWon / (standing.GamesWon + standing.GamesLost);
                }

                standings.Add(standing);
            }

            // Sort based on calculation method
            standings = SortStandings(standings, competition.StandingCalculation);

            return standings;
        }

        private List<TeamStanding> SortStandings(List<TeamStanding> standings, StandingCalculation method)
        {
            return method switch
            {
                StandingCalculation.WinLossPoints => standings
                    .OrderByDescending(s => s.Points)
                    .ThenByDescending(s => s.MatchesWon)
                    .ThenByDescending(s => s.ScoreDifference)
                    .ToList(),

                StandingCalculation.WinPercent => standings
                    .OrderByDescending(s => s.WinPercentage)
                    .ThenByDescending(s => s.MatchesWon)
                    .ThenByDescending(s => s.ScoreDifference)
                    .ToList(),

                StandingCalculation.GameWinPercentage => standings
                    .OrderByDescending(s => s.GameWinPercentage)
                    .ThenByDescending(s => s.GamesWon)
                    .ThenByDescending(s => s.ScoreDifference)
                    .ToList(),

                StandingCalculation.GamesWon => standings
                    .OrderByDescending(s => s.GamesWon)
                    .ThenByDescending(s => s.MatchesWon)
                    .ThenByDescending(s => s.ScoreDifference)
                    .ToList(),

                StandingCalculation.PointDifferential => standings
                    .OrderByDescending(s => s.TotalScore)
                    .ThenByDescending(s => s.MatchesWon)
                    .ThenByDescending(s => s.ScoreDifference)
                    .ToList(),

                _ => standings.OrderByDescending(s => s.MatchesWon).ToList()
            };
        }

        private int[] ParseScores(string scoreString)
        {
            return scoreString.Split(',')
                .Select(s => int.TryParse(s.Trim(), out var val) ? val : 0)
                .ToArray();
        }
    }

    public class TeamStanding
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string PoolName { get; set; } = string.Empty;
        public int Points { get; set; }
        public int GamesPlayed { get; set; }
        public int MatchesWon { get; set; }
        public int MatchesLost { get; set; }
        public int Draws { get; set; }
        
        // ⬇️ ADD: Separate counters for Standard vs Tie-Break
        public int StandardWins { get; set; }
        public int StandardLosses { get; set; }
        public int TieBreakWins { get; set; }
        public int TieBreakLosses { get; set; }
        
        public int GamesWon { get; set; }
        public int GamesLost { get; set; }
        public int TotalScore { get; set; }
        public int ScoreDifference { get; set; }
        public double WinPercentage { get; set; }
        public double GameWinPercentage { get; set; }
        public Dictionary<int, int> HeadToHeadWins { get; set; } = new();
        public Dictionary<int, int> HeadToHeadDifference { get; set; } = new();
    }
}