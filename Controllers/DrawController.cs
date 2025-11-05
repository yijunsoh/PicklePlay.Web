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
            var schedule = await _context.Schedules
                .Include(s => s.Competition)
                .Include(s => s.Teams.Where(t => t.Status == TeamStatus.Confirmed))
                .Include(s => s.Pools)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();
            if (schedule.Competition == null || schedule.Status == ScheduleStatus.PendingSetup)
            {
                TempData["ErrorMessage"] = "Please complete the 'Match Setup & Format' before generating a draw.";
                return RedirectToAction("CompDetails", "Competition", new { id = id });
            }

            var confirmedTeams = schedule.Teams.Where(t => t.Status == TeamStatus.Confirmed).ToList();

            // --- Smart Redirect Logic ---
            switch (schedule.Competition.Format)
            {
                case CompetitionFormat.PoolPlay:
                    // Auto-create pools if they don't exist
                    if (!schedule.Pools.Any())
                    {
                        await CreatePoolsAsync(schedule, confirmedTeams);
                    }
                    var poolViewModel = new DrawPoolPlayViewModel
                    {
                        ScheduleId = schedule.ScheduleId,
                        CompetitionName = schedule.GameName,
                        Pools = await _context.Pools
                                      .Where(p => p.ScheduleId == id)
                                      .Include(p => p.Teams)
                                      .ThenInclude(t => t.Captain)
                                      .ToListAsync(),
                        UnassignedTeams = confirmedTeams.Where(t => !t.PoolId.HasValue).ToList()
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
                        TotalSeeds = schedule.NumTeam ?? confirmedTeams.Count
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

        // --- Pool Play Actions ---
        private async Task CreatePoolsAsync(Schedule schedule, List<Team> teams)
        {
            var numPools = schedule.Competition?.NumPool ?? 4;
            var newPools = new List<Pool>();

            for (int i = 0; i < numPools; i++)
            {
                var pool = new Pool
                {
                    ScheduleId = schedule.ScheduleId,
                    PoolName = $"Pool {(char)('A' + i)}"
                };
                newPools.Add(pool);
            }
            _context.Pools.AddRange(newPools);
            await _context.SaveChangesAsync(); 

            int poolIndex = 0;
            bool forward = true;
            foreach (var team in teams)
            {
                team.PoolId = newPools[poolIndex].PoolId;
                _context.Teams.Update(team);

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
            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePoolDraw(DrawPoolPlayViewModel vm)
        {
            // The form posts a Dictionary<int, int>
            if (vm.TeamPoolSelections == null)
            {
                TempData["ErrorMessage"] = "No changes were submitted.";
                return RedirectToAction("GenerateDraw", new { id = vm.ScheduleId });
            }

            // *** THIS IS THE FIX ***
            // Loop through the KeyValuePair
            foreach (var selection in vm.TeamPoolSelections)
            {
                // Use .Key for TeamId and .Value for PoolId
                var teamId = selection.Key;
                var poolId = selection.Value;

                var team = await _context.Teams.FindAsync(teamId);
                if (team != null)
                {
                    // Assign 0 as null for "Unassigned"
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
                shuffledTeams[i].BracketSeed = i + 1; // Assign seed 1, 2, 3...
                _context.Teams.Update(shuffledTeams[i]);
            }
            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEliminationDraw(DrawEliminationViewModel vm)
        {
            // The form posts a Dictionary<int, int>
            if (vm.SeedAssignments == null)
            {
                TempData["ErrorMessage"] = "No changes were submitted.";
                return RedirectToAction("GenerateDraw", new { id = vm.ScheduleId });
            }

            var allTeams = await _context.Teams
                                 .Where(t => t.ScheduleId == vm.ScheduleId && t.Status == TeamStatus.Confirmed)
                                 .ToListAsync();
            
            // Clear existing seeds
            foreach (var team in allTeams)
            {
                team.BracketSeed = null;
            }
            
            // *** THIS IS THE FIX ***
            // Loop through the KeyValuePair
            foreach (var assignment in vm.SeedAssignments)
            {
                // Use .Key for Seed and .Value for TeamId
                var seed = assignment.Key;
                var teamId = assignment.Value;

                if (teamId == 0) continue; // Skip "Unassigned"

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
    }
}