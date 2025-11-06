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
            // --- *** THIS IS THE FINAL FIX *** ---

            // 1. Load schedule (NO tracking, NO competition)
            var schedule = await _context.Schedules
                .AsNoTracking() // <--- Force fresh read
                .Include(s => s.Teams.Where(t => t.Status == TeamStatus.Confirmed))
                .Include(s => s.Pools)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            // 2. Load Competition in a *separate, non-tracked query*
            //    to guarantee it is fresh from the database.
            var competition = await _context.Competitions
                .AsNoTracking() // <--- Force fresh read
                .FirstOrDefaultAsync(c => c.ScheduleId == id);
            
            // 3. Manually attach the fresh competition data
            schedule.Competition = competition;

            // --- *** END OF FIX *** ---


            // 4. The rest of your method's logic now uses the fresh data
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
                    
                    // *** FIX FOR POOL PLAY ***
                    // Check if the number of pools in the DB matches the competition settings
                    int requiredPools = schedule.Competition.NumPool;
                    if (schedule.Pools.Count != requiredPools)
                    {
                        // Mismatch found! Delete old pools and reset teams.
                        
                        // 1. Reset all teams in this competition
                        foreach (var team in confirmedTeams)
                        {
                            team.PoolId = null;
                        }
                        _context.Teams.UpdateRange(confirmedTeams);

                        // 2. Delete all existing pools for this schedule
                        _context.Pools.RemoveRange(schedule.Pools);
                        
                        await _context.SaveChangesAsync();
                        
                        // 3. Re-create pools and re-assign teams
                        await CreatePoolsAsync(schedule, confirmedTeams);
                        
                        // 4. Reload schedule data with new pools
                        schedule.Pools = await _context.Pools
                                               .Where(p => p.ScheduleId == id)
                                               .ToListAsync();
                    }
                    // *** END OF FIX ***

                    var poolViewModel = new DrawPoolPlayViewModel
                    {
                        ScheduleId = schedule.ScheduleId,
                        CompetitionName = schedule.GameName,
                        Pools = await _context.Pools
                                      .Where(p => p.ScheduleId == id)
                                      .Include(p => p.Teams)
                                      .ThenInclude(t => t.Captain)
                                      .ToListAsync(), // Load fresh data
                        UnassignedTeams = confirmedTeams.Where(t => !t.PoolId.HasValue).ToList(),
                        IsDrawPublished = schedule.Competition.DrawPublished
                    };
                    
                    return View("~/Views/Competition/DrawPoolPlay.cshtml", poolViewModel);

                case CompetitionFormat.Elimination:
                    // Auto-assign seeds if they don't exist
                    if (confirmedTeams.Any(t => !t.BracketSeed.HasValue))
                    {
                        await AssignSeedsAsync(confirmedTeams);
                    }
                      // *** ADD THESE DEBUG LINES ***
    Console.WriteLine($"DEBUG: Competition.ThirdPlaceMatch = {schedule.Competition.ThirdPlaceMatch}");


                    var elimViewModel = new DrawEliminationViewModel
                    {
                        ScheduleId = schedule.ScheduleId,
                        CompetitionName = schedule.GameName,
                        Teams = confirmedTeams.OrderBy(t => t.BracketSeed).ToList(),
                        TotalSeeds = schedule.NumTeam ?? confirmedTeams.Count,

                        // This will now use the fresh data from step 2
                        HasThirdPlaceMatch = schedule.Competition.ThirdPlaceMatch,
                        IsDrawPublished = schedule.Competition.DrawPublished
                    };
                    
                    // *** ADD THIS DEBUG LINE ***
    Console.WriteLine($"DEBUG: ViewModel.HasThirdPlaceMatch = {elimViewModel.HasThirdPlaceMatch}");
                    
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
    try
    {
        Console.WriteLine($"ViewPublishedDraw called for Schedule ID: {id}");
        
        // 1. Load schedule
        var schedule = await _context.Schedules
            .AsNoTracking()
            .Include(s => s.Teams.Where(t => t.Status == TeamStatus.Confirmed))
            .ThenInclude(t => t.Captain)
            .FirstOrDefaultAsync(s => s.ScheduleId == id);

        if (schedule == null)
        {
            Console.WriteLine("ERROR: Schedule not found");
            return PartialView("~/Views/Competition/_ViewDrawError.cshtml", "Competition not found.");
        }

        // 2. Load Competition separately
        var competition = await _context.Competitions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ScheduleId == id);
            
        schedule.Competition = competition;

        if (schedule.Competition == null)
        {
            Console.WriteLine("ERROR: Competition details missing");
            return PartialView("~/Views/Competition/_ViewDrawError.cshtml", "Competition details are missing.");
        }
        
        Console.WriteLine($"Competition Format: {schedule.Competition.Format}");
        Console.WriteLine($"Draw Published: {schedule.Competition.DrawPublished}");
        
        if (!schedule.Competition.DrawPublished)
        {
            Console.WriteLine("ERROR: Draw not published");
            return PartialView("~/Views/Competition/_ViewDrawError.cshtml", "The draw has not been published yet.");
        }

        var confirmedTeams = schedule.Teams.Where(t => t.Status == TeamStatus.Confirmed).ToList();
        Console.WriteLine($"Confirmed Teams Count: {confirmedTeams.Count}");

        switch (schedule.Competition.Format)
        {
            case CompetitionFormat.PoolPlay:
                Console.WriteLine("Loading Pool Play draw...");
                
                var pools = await _context.Pools
                    .AsNoTracking()
                    .Where(p => p.ScheduleId == id)
                    .Include(p => p.Teams)
                    .ThenInclude(t => t.Captain)
                    .ToListAsync();

                Console.WriteLine($"Pools loaded: {pools.Count}");

                var poolViewModel = new DrawPoolPlayViewModel
                {
                    ScheduleId = schedule.ScheduleId,
                    CompetitionName = schedule.GameName,
                    Pools = pools,
                    UnassignedTeams = new List<Team>()
                };
                
                return PartialView("~/Views/Competition/_ViewDrawPoolPlay.cshtml", poolViewModel);

            case CompetitionFormat.Elimination:
                Console.WriteLine("Loading Elimination draw...");
                Console.WriteLine($"HasThirdPlaceMatch: {schedule.Competition.ThirdPlaceMatch}");
                
                var elimViewModel = new DrawEliminationViewModel
                {
                    ScheduleId = schedule.ScheduleId,
                    CompetitionName = schedule.GameName,
                    Teams = confirmedTeams.OrderBy(t => t.BracketSeed).ToList(),
                    TotalSeeds = schedule.NumTeam ?? confirmedTeams.Count,
                    HasThirdPlaceMatch = schedule.Competition.ThirdPlaceMatch
                };
                
                Console.WriteLine($"ViewModel created with {elimViewModel.Teams.Count} teams");
                
                return PartialView("~/Views/Competition/_ViewDrawElimination.cshtml", elimViewModel);

            case CompetitionFormat.RoundRobin:
                Console.WriteLine("Round Robin - no draw needed");
                return PartialView("~/Views/Competition/_ViewDrawError.cshtml", "Draws are not applicable for Round Robin format.");

            default:
                Console.WriteLine($"ERROR: Unknown format - {schedule.Competition.Format}");
                return PartialView("~/Views/Competition/_ViewDrawError.cshtml", "Unknown competition format.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"EXCEPTION in ViewPublishedDraw: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        
        if (ex.InnerException != null)
        {
            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
        }
        
        return PartialView("~/Views/Competition/_ViewDrawError.cshtml", $"An error occurred: {ex.Message}");
    }
}
    }
}