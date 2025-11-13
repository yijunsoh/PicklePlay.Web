using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    public class DrawController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DrawController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GenerateDraw(int id)
        {
            // 1. Load schedule with confirmed teams
            var schedule = await _context.Schedules
                .Include(s => s.Teams.Where(t => t.Status == TeamStatus.Confirmed))
                    .ThenInclude(t => t.Captain)
                .Include(s => s.Pools)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            // 2. Load Competition in a *separate query*
            var competition = await _context.Competitions
                .FirstOrDefaultAsync(c => c.ScheduleId == id);
            
            schedule.Competition = competition;

            // 3. Check if competition setup is complete
            if (schedule.Competition == null || schedule.Status == ScheduleStatus.PendingSetup)
            {
                TempData["ErrorMessage"] = "Please complete the 'Match Setup & Format' before generating a draw.";
                return RedirectToAction("CompDetails", "Competition", new { id = id });
            }

            var confirmedTeams = schedule.Teams.Where(t => t.Status == TeamStatus.Confirmed).ToList();

            // 4. Add validation for confirmed teams
            if (!confirmedTeams.Any())
            {
                TempData["ErrorMessage"] = "No confirmed teams available to generate draw.";
                return RedirectToAction("CompDetails", "Competition", new { id = id });
            }

            // --- Smart Redirect Logic ---
            switch (schedule.Competition.Format)
            {
                case CompetitionFormat.PoolPlay:
                    
                    int requiredPools = schedule.Competition.NumPool;
                    
                    // Only recreate pools if needed
                    if (schedule.Pools.Count != requiredPools)
                    {
                        // 1. Reset all teams in this schedule
                        var allTeamsInSchedule = await _context.Teams
                            .Where(t => t.ScheduleId == id)
                            .ToListAsync();
                            
                        foreach (var team in allTeamsInSchedule)
                        {
                            team.PoolId = null;
                        }
                        _context.Teams.UpdateRange(allTeamsInSchedule);

                        // 2. Delete all existing pools for this schedule
                        var existingPools = await _context.Pools
                            .Where(p => p.ScheduleId == id)
                            .ToListAsync();
                        _context.Pools.RemoveRange(existingPools);
                        
                        await _context.SaveChangesAsync();
                        
                        // 3. Re-create pools and re-assign confirmed teams
                        await CreatePoolsAsync(schedule, confirmedTeams);
                    }

                    // *** FIX: Load pools and their teams using the PoolId foreign key ***
                    var poolsForView = await _context.Pools
                        .Where(p => p.ScheduleId == id)
                        .ToListAsync();

                    // Create a dictionary to hold teams for each pool
                    var teamsGroupedByPool = await _context.Teams
                        .Where(t => t.ScheduleId == id && t.PoolId.HasValue && t.Status == TeamStatus.Confirmed)
                        .Include(t => t.Captain)
                        .ToListAsync();

                    // Assign teams to their respective pools
                    foreach (var pool in poolsForView)
                    {
                        // Get teams for this specific pool
                        pool.Teams = teamsGroupedByPool
                            .Where(t => t.PoolId == pool.PoolId)
                            .ToList();
                    }

                    var poolViewModel = new DrawPoolPlayViewModel
                    {
                        ScheduleId = schedule.ScheduleId,
                        CompetitionName = schedule.GameName,
                        Pools = poolsForView,
                        UnassignedTeams = await _context.Teams
                            .Where(t => t.ScheduleId == id && 
                                       t.Status == TeamStatus.Confirmed && 
                                       !t.PoolId.HasValue)
                            .Include(t => t.Captain)
                            .ToListAsync(),
                        IsDrawPublished = schedule.Competition.DrawPublished
                    };
                    
                    return View("~/Views/Competition/DrawPoolPlay.cshtml", poolViewModel);

                case CompetitionFormat.Elimination:
                    // Auto-assign seeds if they don't exist
                    if (confirmedTeams.Any(t => !t.BracketSeed.HasValue))
                    {
                        await AssignSeedsAsync(confirmedTeams);
                    }

                    var elimViewModel = new DrawEliminationViewModel
                    {
                        ScheduleId = schedule.ScheduleId,
                        CompetitionName = schedule.GameName,
                        Teams = confirmedTeams.OrderBy(t => t.BracketSeed).ToList(),
                        TotalSeeds = confirmedTeams.Count,
                        HasThirdPlaceMatch = schedule.Competition.ThirdPlaceMatch,
                        IsDrawPublished = schedule.Competition.DrawPublished
                    };
                    
                    return View("~/Views/Competition/DrawElimination.cshtml", elimViewModel);

                case CompetitionFormat.RoundRobin:
                    TempData["SuccessMessage"] = "Round Robin format does not require a manual draw.";
                    return RedirectToAction("CompDetails", "Competition", new { id = id });

                default:
                    TempData["ErrorMessage"] = "Unknown competition format.";
                    return RedirectToAction("CompDetails", "Competition", new { id = id });
            }
        }

        // --- *** START: NEW ACTION TO PUBLISH DRAW *** ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PublishDraw(int id)
        {
            var competition = await _context.Competitions.FirstOrDefaultAsync(c => c.ScheduleId == id);
            if (competition != null)
            {
                // You will need to add this 'DrawPublished' property to your Competition.cs model file
                // e.g., public bool DrawPublished { get; set; } = false;
                competition.DrawPublished = true;
                _context.Update(competition);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Draw has been published! It is now visible to participants.";
            }
            else
            {
                TempData["ErrorMessage"] = "Could not find competition to publish.";
            }
            return RedirectToAction("GenerateDraw", new { id = id });
        }
        // --- *** END: NEW ACTION *** ---
// --- *** START: NEW ACTION TO UNPUBLISH DRAW *** ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnpublishDraw(int id)
        {
            var competition = await _context.Competitions.FirstOrDefaultAsync(c => c.ScheduleId == id);
            if (competition != null)
            {
                competition.DrawPublished = false;
                _context.Update(competition);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Draw has been unpublished and is no longer visible to participants.";
            }
            else
            {
                TempData["ErrorMessage"] = "Could not find competition to unpublish.";
            }
            return RedirectToAction("GenerateDraw", new { id = id });
        }
        // --- *** END: NEW ACTION *** ---

        // --- Pool Play Actions ---
        private async Task CreatePoolsAsync(Schedule schedule, List<Team> teams)
        {
            if (!teams.Any())
            {
                return; // No teams to assign
            }

            var numPools = schedule.Competition?.NumPool ?? 4;
            var newPools = new List<Pool>();

            // Create pools
            for (int i = 0; i < numPools; i++)
            {
                var pool = new Pool
                {
                    ScheduleId = schedule.ScheduleId,
                    PoolName = $"Pool {(char)('A' + i)}",
                    Teams = new List<Team>() // Initialize the collection
                };
                newPools.Add(pool);
            }
            _context.Pools.AddRange(newPools);
            await _context.SaveChangesAsync(); // Save pools first to get IDs

            // Snake draft distribution
            int poolIndex = 0;
            bool forward = true;
            
            foreach (var team in teams)
            {
                team.PoolId = newPools[poolIndex].PoolId;

                if (forward)
                {
                    if (poolIndex == numPools - 1)
                        forward = false;
                    else
                        poolIndex++;
                }
                else
                {
                    if (poolIndex == 0)
                        forward = true;
                    else
                        poolIndex--;
                }
            }
            
            _context.Teams.UpdateRange(teams);
            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePoolDraw(DrawPoolPlayViewModel vm)
        {
            if (vm.TeamPoolSelections == null)
            {
                TempData["ErrorMessage"] = "No changes were submitted.";
                return RedirectToAction("GenerateDraw", new { id = vm.ScheduleId });
            }

            foreach (var selection in vm.TeamPoolSelections)
            {
                var teamId = selection.Key;
                var poolId = selection.Value;

                var team = await _context.Teams.FindAsync(teamId);
                if (team != null)
                {
                    team.PoolId = (poolId == 0) ? (int?)null : poolId;
                    _context.Teams.Update(team);
                }
            }
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Pool draw has been saved!";
            return RedirectToAction("GenerateDraw", new { id = vm.ScheduleId });
        }


        // --- Elimination Actions ---
        private async Task AssignSeedsAsync(List<Team> teams)
        {
            var shuffledTeams = teams.OrderBy(t => Guid.NewGuid()).ToList();
            for (int i = 0; i < shuffledTeams.Count; i++)
            {
                shuffledTeams[i].BracketSeed = i + 1;
                _context.Teams.Update(shuffledTeams[i]);
            }
            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEliminationDraw(DrawEliminationViewModel vm)
        {
            if (vm.SeedAssignments == null)
            {
                TempData["ErrorMessage"] = "No changes were submitted.";
                return RedirectToAction("GenerateDraw", new { id = vm.ScheduleId });
            }

            var allTeams = await _context.Teams
                                 .Where(t => t.ScheduleId == vm.ScheduleId && t.Status == TeamStatus.Confirmed)
                                 .ToListAsync();

            foreach (var team in allTeams)
            {
                team.BracketSeed = null;
            }

            foreach (var assignment in vm.SeedAssignments)
            {
                var seed = assignment.Key;
                var teamId = assignment.Value;

                if (teamId == 0) continue;

                var team = allTeams.FirstOrDefault(t => t.TeamId == teamId);
                if (team != null)
                {
                    team.BracketSeed = seed;
                }
            }

            _context.Teams.UpdateRange(allTeams);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Elimination draw has been saved!";
            return RedirectToAction("GenerateDraw", new { id = vm.ScheduleId });
        }

        // Replace your ViewPublishedDraw action with this fixed version:

[HttpGet]
public async Task<IActionResult> ViewPublishedDraw(int id)
{
    var competition = await _context.Competitions
        .FirstOrDefaultAsync(c => c.ScheduleId == id);

    if (competition == null || !competition.DrawPublished)
    {
        return PartialView("~/Views/Competition/_ViewDrawEmpty.cshtml");
    }

    if (competition.Format == CompetitionFormat.PoolPlay)
    {
        // Load pools
        var pools = await _context.Pools
            .Where(p => p.ScheduleId == id)
            .ToListAsync();

        // Load all teams with PoolId
        var allTeams = await _context.Teams
            .Where(t => t.ScheduleId == id && t.PoolId.HasValue && t.Status == TeamStatus.Confirmed)
            .Include(t => t.Captain)
            .ToListAsync();

        // Assign teams to pools
        foreach (var pool in pools)
        {
            pool.Teams = allTeams.Where(t => t.PoolId == pool.PoolId).ToList();
        }

        var vm = new DrawPoolPlayViewModel
        {
            ScheduleId = id,
            Pools = pools,
            IsDrawPublished = true
        };

        return PartialView("~/Views/Competition/_ViewDrawPoolPlay.cshtml", vm);
    }
    else if (competition.Format == CompetitionFormat.Elimination)
    {
        // ...existing elimination code...
    }

    return PartialView("~/Views/Competition/_ViewDrawEmpty.cshtml");
}
    }
}