using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace PicklePlay.Controllers
{
    // You should secure this controller
    // [Authorize]
    public class MatchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public MatchController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }
        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        // GET: /Match/GetConfirmationDetails/5
        [HttpGet]
        public async Task<IActionResult> GetConfirmationDetails(int id)
        {
            var schedule = await _context.Schedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            var competition = await _context.Competitions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ScheduleId == id);

            if (schedule == null || competition == null) return NotFound();

            var vm = new StartCompetitionConfirmViewModel
            {
                Schedule = schedule,
                Competition = competition
            };

            // Load draw details for the preview
            if (competition.Format == CompetitionFormat.PoolPlay)
            {
                vm.PoolDraw = new DrawPoolPlayViewModel
                {
                    Pools = await _context.Pools
                                      .AsNoTracking()
                                      .Where(p => p.ScheduleId == id)
                                      .Include(p => p.Teams)
                                      .ToListAsync()
                };
            }
            else if (competition.Format == CompetitionFormat.Elimination)
            {
                vm.EliminationDraw = new DrawEliminationViewModel
                {
                    Teams = await _context.Teams
                                        .AsNoTracking()
                                        .Where(t => t.ScheduleId == id && t.Status == TeamStatus.Confirmed)
                                        .OrderBy(t => t.BracketSeed)
                                        .ToListAsync()
                };
            }

            return PartialView("~/Views/Competition/_StartConfirm.cshtml", vm);
        }


        // POST: /Match/StartCompetition/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartCompetition(int id)
        {
            var schedule = await _context.Schedules.FindAsync(id);
            var competition = await _context.Competitions.FirstOrDefaultAsync(c => c.ScheduleId == id);

            if (schedule == null || competition == null) return NotFound();

            // Final checks before starting
            if (competition.Format != CompetitionFormat.RoundRobin && !competition.DrawPublished)
            {
                TempData["ErrorMessage"] = "Please publish the draw to proceed.";
                return RedirectToAction("CompDetails", "Competition", new { id = id });
            }

            var matches = new List<Match>();
            var confirmedTeams = await _context.Teams
                .Where(t => t.ScheduleId == id && t.Status == TeamStatus.Confirmed)
                .ToListAsync();

            switch (competition.Format)
            {
                case CompetitionFormat.PoolPlay:
                    matches = await GeneratePoolPlayMatches(id, competition);
                    break;
                case CompetitionFormat.Elimination:
                    matches = GenerateEliminationMatches(confirmedTeams, id);
                    break;
                case CompetitionFormat.RoundRobin:
                    matches = GenerateRoundRobinMatches(confirmedTeams, id);
                    break;
            }

            // Save all generated matches
            await _context.Matches.AddRangeAsync(matches);

            // Update schedule status to "In Progress"
            schedule.Status = ScheduleStatus.InProgress;
            _context.Schedules.Update(schedule);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Competition Started! Match listing is now available.";
            // Redirect to the match tab
            return RedirectToAction("CompDetails", "Competition", new { id = id, tab = "match-listing" });
        }


        private async Task<List<Match>> GeneratePoolPlayMatches(int scheduleId, Competition competition)
        {
            var matches = new List<Match>();
            var pools = await _context.Pools
                .Where(p => p.ScheduleId == scheduleId)
                .Include(p => p.Teams.Where(t => t.Status == TeamStatus.Confirmed))
                .ToListAsync();

            foreach (var pool in pools)
            {
                var teamsInPool = pool.Teams.ToList();
                int matchNumber = 1;
                for (int i = 0; i < teamsInPool.Count; i++)
                {
                    for (int j = i + 1; j < teamsInPool.Count; j++)
                    {
                        matches.Add(new Match
                        {
                            ScheduleId = scheduleId,
                            Team1Id = teamsInPool[i].TeamId,
                            Team2Id = teamsInPool[j].TeamId,
                            RoundName = pool.PoolName,
                            RoundNumber = 1,
                            MatchNumber = matchNumber++,
                            Status = MatchStatus.Active
                        });
                    }
                }
            }
            return matches;
        }

        private List<Match> GenerateEliminationMatches(List<Team> teams, int scheduleId)
        {
            var matches = new List<Match>();
            var seededTeams = teams.OrderBy(t => t.BracketSeed).ToDictionary(t => t.BracketSeed ?? 0, t => t);

            // Get standard seeding order
            int bracketSize = 8;
            if (teams.Count > 32) bracketSize = 64;
            else if (teams.Count > 16) bracketSize = 32;
            else if (teams.Count > 8) bracketSize = 16;

            var seeding = GetSeedingOrder(bracketSize);

            int matchNumber = 1;
            for (int i = 0; i < seeding.Length; i += 2)
            {
                seededTeams.TryGetValue(seeding[i], out var team1);
                seededTeams.TryGetValue(seeding[i + 1], out var team2);

                var match = new Match
                {
                    ScheduleId = scheduleId,
                    RoundName = $"Round of {bracketSize}",
                    RoundNumber = 1,
                    MatchNumber = matchNumber++,
                    Status = MatchStatus.Active
                };

                if (team1 != null && team2 != null)
                {
                    match.Team1Id = team1.TeamId;
                    match.Team2Id = team2.TeamId;
                }
                else if (team1 != null && team2 == null) // Team 2 is a BYE
                {
                    match.Team1Id = team1.TeamId;
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = team1.TeamId;
                }
                else if (team1 == null && team2 != null) // Team 1 is a BYE
                {
                    match.Team2Id = team2.TeamId;
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = team2.TeamId;
                }
                else // Both are BYEs (unlikely, but possible in sparse bracket)
                {
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                }
                matches.Add(match);
            }
            return matches;
        }

        private List<Match> GenerateRoundRobinMatches(List<Team> teams, int scheduleId)
        {
            var matches = new List<Match>();
            int matchNumber = 1;
            for (int i = 0; i < teams.Count; i++)
            {
                for (int j = i + 1; j < teams.Count; j++)
                {
                    matches.Add(new Match
                    {
                        ScheduleId = scheduleId,
                        Team1Id = teams[i].TeamId,
                        Team2Id = teams[j].TeamId,
                        RoundName = "Round Robin",
                        RoundNumber = 1,
                        MatchNumber = matchNumber++,
                        Status = MatchStatus.Active
                    });
                }
            }
            return matches;
        }

        private int[] GetSeedingOrder(int size)
        {
            switch (size)
            {
                case 16: return new[] { 1, 16, 8, 9, 5, 12, 4, 13, 6, 11, 3, 14, 7, 10, 2, 15 };
                case 32: return new[] { 1, 32, 17, 16, 9, 24, 25, 8, 5, 28, 21, 12, 13, 20, 29, 4, 3, 30, 19, 14, 11, 22, 27, 6, 7, 26, 23, 10, 15, 18, 31, 2 };
                case 64: // simplified for brevity
                    var seeds = new List<int>();
                    for (int i = 0; i < 32; i++) { seeds.Add(i + 1); seeds.Add(64 - i); }
                    return seeds.ToArray();
                case 8:
                default:
                    return new[] { 1, 8, 4, 5, 3, 6, 2, 7 };
            }
        }

        // GET: /Match/GetMatchListing/5
        [HttpGet]
        public async Task<IActionResult> GetMatchListing(int id)
        {
            var schedule = await _context.Schedules
                .Include(s => s.Participants) // <-- This is needed for the organizer check
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            var matches = await _context.Matches
                .Where(m => m.ScheduleId == id)
                .Include(m => m.Team1)
                .Include(m => m.Team2)
                .OrderBy(m => m.RoundNumber)
                .ThenBy(m => m.RoundName)
                .ThenBy(m => m.MatchNumber)
                .ToListAsync();

            // *** GET THE CURRENT USER ID (THE CORRECT WAY) ***
            int? currentUserId = GetCurrentUserId();

            // *** THIS CHECK NOW WORKS FOR ALL ORGANIZERS/STAFF ***
            bool isOrganizer = false;
            if (currentUserId.HasValue)
            {
                isOrganizer = schedule.Participants.Any(p =>
                    p.UserId == currentUserId.Value &&
                    p.Role == ParticipantRole.Organizer
                );
            }

            var vm = new MatchListingViewModel
            {
                ScheduleId = id,
                Matches = matches,
                IsOrganizer = isOrganizer // <-- This will now be 'true' for you and your staff
            };

            return PartialView("~/Views/Competition/_MatchListing.cshtml", vm);
        }

        // In Controllers/MatchController.cs

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMatch(int MatchId, string Team1Score, string Team2Score)
        {
            var match = await _context.Matches.FindAsync(MatchId);
            if (match == null)
            {
                return NotFound();
            }

            match.Team1Score = Team1Score;
            match.Team2Score = Team2Score;

            // Determine the winner
            match.WinnerId = DetermineWinnerId(match.Team1Id, match.Team2Id, Team1Score, Team2Score);

            // *** NEW LOGIC ***
            // Check if any score set is not "0" AND not empty.
            bool hasRealScores = Team1Score.Split(',').Any(s => s.Trim() != "0" && !string.IsNullOrEmpty(s.Trim())) ||
                                 Team2Score.Split(',').Any(s => s.Trim() != "0" && !string.IsNullOrEmpty(s.Trim()));

            // Only set status to Done if real scores were submitted.
            if (hasRealScores && match.Status != MatchStatus.Bye)
            {
                match.Status = MatchStatus.Done;
            }
            // If no real scores were submitted (e.g., "0,0" from the Start button bug),
            // the status will remain unchanged (e.g., Progressing).
            // *** END NEW LOGIC ***

            _context.Matches.Update(match);
            await _context.SaveChangesAsync();

            // Redirect back to the match listing
            return RedirectToAction("CompDetails", "Competition", new { id = match.ScheduleId, tab = "match-listing" });
        }

        // A helper method to determine the winner based on scores
        private int? DetermineWinnerId(int? team1Id, int? team2Id, string team1Score, string team2Score)
        {
            var setScores1 = team1Score.Split(',').Select(s => int.TryParse(s.Trim(), out var val) ? val : 0).ToList();
            var setScores2 = team2Score.Split(',').Select(s => int.TryParse(s.Trim(), out var val) ? val : 0).ToList();

            int team1SetWins = 0;
            int team2SetWins = 0;

            int sets = Math.Min(setScores1.Count, setScores2.Count);

            for (int i = 0; i < sets; i++)
            {
                if (setScores1[i] > setScores2[i])
                {
                    team1SetWins++;
                }
                else if (setScores2[i] > setScores1[i])
                {
                    team2SetWins++;
                }
            }

            if (team1SetWins > team2SetWins)
            {
                return team1Id;
            }
            if (team2SetWins > team1SetWins)
            {
                return team2Id;
            }

            return null; // It's a draw or scores are invalid
        }

        // In Controllers/MatchController.cs

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMatchStatus(int MatchId, MatchStatus Status)
        {
            var match = await _context.Matches.FindAsync(MatchId);
            if (match == null)
            {
                return NotFound();
            }

            // *** FIX: Removed the "if" block ***
            // Now, it will set the status no matter what.
            // This allows the "Edit Score" button (which passes "Progressing")
            // to correctly update a "Done" match.
            match.Status = Status;

            _context.Matches.Update(match);
            await _context.SaveChangesAsync();

            return RedirectToAction("CompDetails", "Competition", new { id = match.ScheduleId, tab = "match-listing" });
        }
        // In Controllers/MatchController.cs

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMatchDetails(int MatchId, DateTime? MatchTime, string Court)
        {
            var match = await _context.Matches.FindAsync(MatchId);
            if (match == null)
            {
                return NotFound();
            }

            match.MatchTime = MatchTime;
            match.Court = Court;

            _context.Matches.Update(match);
            await _context.SaveChangesAsync();

            return RedirectToAction("CompDetails", "Competition", new { id = match.ScheduleId, tab = "match-listing" });
        }



    }
}