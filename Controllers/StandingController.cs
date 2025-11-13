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
    }
}