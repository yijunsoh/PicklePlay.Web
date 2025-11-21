using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Application.Services;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;

namespace PicklePlay.Controllers
{
    public class StandingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly StandingCalculationService _standingService;

        public StandingController(ApplicationDbContext context)
        {
            _context = context;
            _standingService = new StandingCalculationService(context);
        }

        [HttpGet]
        public async Task<IActionResult> GetPoolStandings(int id)
        {
            Console.WriteLine($"GetPoolStandings called with id: {id}");
            
            var competition = await _context.Competitions
                .FirstOrDefaultAsync(c => c.ScheduleId == id);

            if (competition == null || competition.Format != CompetitionFormat.PoolPlay)
            {
                Console.WriteLine($"Competition not found or wrong format. Found: {competition != null}, Format: {competition?.Format}");
                return PartialView("~/Views/Competition/_EmptyStanding.cshtml");
            }

            Console.WriteLine($"Competition found: {competition.ScheduleId}, Format: {competition.Format}");

            var pools = await _context.Pools
                .Where(p => p.ScheduleId == id)
                .OrderBy(p => p.PoolName)
                .ToListAsync();

            Console.WriteLine($"Pools found: {pools.Count}");

            var viewModel = new PoolPlayStandingsViewModel
            {
                ScheduleId = id,
                CalculationMethod = competition.StandingCalculation,
                Competition = competition,
                PoolGroups = new List<PoolStandingGroup>()
            };

            bool allPoolsComplete = true;

            foreach (var pool in pools)
            {
                Console.WriteLine($"Processing pool: {pool.PoolName} (ID: {pool.PoolId})");
                
                var standings = await _standingService.CalculatePoolStandings(pool.PoolId, competition);
                Console.WriteLine($"Standings calculated for {pool.PoolName}: {standings.Count} teams");
                
                var poolMatches = await _context.Matches
                    .Where(m => m.RoundName == pool.PoolName)
                    .ToListAsync();
                
                bool poolComplete = poolMatches.Any() && poolMatches.All(m => m.Status == MatchStatus.Done);
                Console.WriteLine($"Pool {pool.PoolName} complete: {poolComplete} ({poolMatches.Count} matches)");
                
                if (!poolComplete) allPoolsComplete = false;

                viewModel.PoolGroups.Add(new PoolStandingGroup
                {
                    PoolName = pool.PoolName!,
                    PoolId = pool.PoolId,
                    Standings = standings,
                    AdvancingTeams = competition.WinnersPerPool,
                    IsComplete = poolComplete
                });
            }

            viewModel.IsPoolStageComplete = allPoolsComplete;

            Console.WriteLine($"Returning view with {viewModel.PoolGroups.Count} pool groups");

            return PartialView("~/Views/Competition/_PoolStandings.cshtml", viewModel);
        }

                [HttpGet]
        public async Task<IActionResult> GetRoundRobinStandings(int id)
        {
            Console.WriteLine($"GetRoundRobinStandings called with id: {id}");

            var competition = await _context.Competitions
                .FirstOrDefaultAsync(c => c.ScheduleId == id);

            if (competition == null || competition.Format != CompetitionFormat.RoundRobin)
            {
                Console.WriteLine("Competition not found or not round robin");
                return PartialView("~/Views/Competition/_EmptyStanding.cshtml");
            }

            // Get confirmed teams for this schedule
            var teams = await _context.Teams
                .Where(t => t.ScheduleId == id && t.Status == TeamStatus.Confirmed)
                .ToListAsync();

            var matches = await _context.Matches
                .Where(m => m.ScheduleId == id && !m.IsThirdPlaceMatch && m.Status == MatchStatus.Done)
                .ToListAsync();

            var standings = new List<RRTeamStanding>();

            foreach (var team in teams)
            {
                var st = new RRTeamStanding
                {
                    TeamId = team.TeamId,
                    TeamName = team.TeamName ?? "TBD"
                };

                var teamMatches = matches.Where(m => m.Team1Id == team.TeamId || m.Team2Id == team.TeamId).ToList();

                foreach (var match in teamMatches)
                {
                    bool isTeam1 = match.Team1Id == team.TeamId;
                    var teamScoreStr = isTeam1 ? match.Team1Score : match.Team2Score;
                    var oppScoreStr = isTeam1 ? match.Team2Score : match.Team1Score;

                    if (string.IsNullOrEmpty(teamScoreStr) || string.IsNullOrEmpty(oppScoreStr))
                        continue;

                    var teamSets = teamScoreStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();
                    var oppSets = oppScoreStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();

                    int setWins = 0, oppSetWins = 0;
                    int teamTotal = 0, oppTotal = 0;
                    for (int i = 0; i < Math.Min(teamSets.Length, oppSets.Length); i++)
                    {
                        if (teamSets[i] > oppSets[i]) setWins++;
                        else if (oppSets[i] > teamSets[i]) oppSetWins++;

                        teamTotal += teamSets[i];
                        oppTotal += oppSets[i];
                    }

                    st.GamesPlayed++;
                    st.GamesWon += setWins;
                    st.GamesLost += oppSetWins;
                    st.ScoreDifference += (teamTotal - oppTotal);

                    if (match.WinnerId == team.TeamId) st.MatchesWon++;
                    else if (match.WinnerId.HasValue) st.MatchesLost++;
                }

                standings.Add(st);
            }

            // Sort: MatchesWon desc, GamesWon desc, GamesLost asc, ScoreDifference desc
            standings = standings
                .OrderByDescending(s => s.MatchesWon)
                .ThenByDescending(s => s.GamesWon)
                .ThenBy(s => s.GamesLost)
                .ThenByDescending(s => s.ScoreDifference)
                .ToList();

            var vm = new RRStandingsViewModel
            {
                ScheduleId = id,
                Standings = standings
            };

            return PartialView("~/Views/Competition/_RRStandings.cshtml", vm);
        }
    }
}