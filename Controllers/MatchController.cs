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

            if (competition.Format != CompetitionFormat.RoundRobin && !competition.DrawPublished)
            {
                TempData["ErrorMessage"] = "Please publish the draw to proceed.";
                return RedirectToAction("CompDetails", "Competition", new { id = id });
            }

            var confirmedTeams = await _context.Teams
                .Where(t => t.ScheduleId == id && t.Status == TeamStatus.Confirmed)
                .ToListAsync();

            // Clear any old matches if they exist (e.g., from a re-started competition)
            var oldMatches = await _context.Matches.Where(m => m.ScheduleId == id).ToListAsync();
            if (oldMatches.Any())
            {
                _context.Matches.RemoveRange(oldMatches);
                await _context.SaveChangesAsync();
            }

            List<Match> matches;
            switch (competition.Format)
            {
                case CompetitionFormat.PoolPlay:
                    matches = await GeneratePoolPlayMatches(id, competition);
                    await _context.Matches.AddRangeAsync(matches);
                    break;
                case CompetitionFormat.Elimination:
                    // This function now saves matches directly to DB
                    await GenerateEliminationBracket(confirmedTeams, id, competition.ThirdPlaceMatch);
                    break;
                case CompetitionFormat.RoundRobin:
                    matches = GenerateRoundRobinMatches(confirmedTeams, id);
                    await _context.Matches.AddRangeAsync(matches);
                    break;
            }

            // Update schedule status to "In Progress"
            schedule.Status = ScheduleStatus.InProgress;
            _context.Schedules.Update(schedule);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Competition Started! Match listing is now available.";
            return RedirectToAction("CompDetails", "Competition", new { id = id, tab = "match-listing" });
        }


        private async Task<List<Match>> GeneratePoolPlayMatches(int scheduleId, Competition competition)
        {
            var matches = new List<Match>();
            
            // *** FIX: Load teams correctly ***
            var pools = await _context.Pools
                .Where(p => p.ScheduleId == scheduleId)
                .ToListAsync();

            // Load all confirmed teams for this schedule
            var allTeams = await _context.Teams
                .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
                .ToListAsync();

            foreach (var pool in pools)
            {
                // Get teams assigned to this pool
                var teamsInPool = allTeams.Where(t => t.PoolId == pool.PoolId).ToList();
                
                if (teamsInPool.Count < 2)
                {
                    continue; // Skip pool if less than 2 teams
                }

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

        private async Task GenerateEliminationBracket(List<Team> teams, int scheduleId, bool hasThirdPlaceMatch)
        {
            var seededTeams = teams.OrderBy(t => t.BracketSeed).ToDictionary(t => t.BracketSeed ?? 0, t => t);

            int bracketSize;
            if (teams.Count <= 8) bracketSize = 8;
            else if (teams.Count <= 16) bracketSize = 16;
            else if (teams.Count <= 32) bracketSize = 32;
            else bracketSize = 64;

            var seeding = GetSeedingOrder(bracketSize);

            var allMatches = new List<Match>();
            var currentRoundMatches = new List<Match>();
            var nextLinkPairs = new List<(Match child, Match parent)>();
            var loserLinkPairs = new List<(Match child, Match parent)>();

            // Store Semi-Finals for 3rd place linking
            List<Match>? semiFinalMatches = null;

            // 2. Create Round 1
            int roundNumber = 1;
            int matchNumber = 1;
            for (int i = 0; i < seeding.Length; i += 2)
            {
                seededTeams.TryGetValue(seeding[i], out var team1);
                seededTeams.TryGetValue(seeding[i + 1], out var team2);

                int position = (matchNumber % 2 == 1) ? 1 : 2;

                var match = new Match
                {
                    ScheduleId = scheduleId,
                    RoundName = $"Round of {bracketSize}",
                    RoundNumber = roundNumber,
                    MatchNumber = matchNumber,
                    Team1Id = team1?.TeamId,
                    Team2Id = team2?.TeamId,
                    MatchPosition = position
                };
                matchNumber++;

                if (team1 != null && team2 == null)
                {
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = team1.TeamId;
                }
                else if (team1 == null && team2 != null)
                {
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = team2.TeamId;
                }
                else
                {
                    match.Status = MatchStatus.Active;
                }

                allMatches.Add(match);
                currentRoundMatches.Add(match);
            }

            // 3. Create subsequent rounds (Quarter-Finals, Semi-Finals, Final)
            int teamsInRound = bracketSize / 2;
            roundNumber++;

            while (teamsInRound >= 2)
            {
                var nextRoundMatches = new List<Match>();
                string roundName = GetRoundName(teamsInRound);
                matchNumber = 1;

                for (int i = 0; i < currentRoundMatches.Count; i += 2)
                {
                    var match1 = currentRoundMatches[i];
                    var match2 = currentRoundMatches[i + 1];

                    int pos = (matchNumber % 2 == 1) ? 1 : 2;

                    var nextMatch = new Match
                    {
                        ScheduleId = scheduleId,
                        RoundName = roundName,
                        RoundNumber = roundNumber,
                        MatchNumber = matchNumber,
                        Status = MatchStatus.Active,
                        MatchPosition = pos
                    };
                    matchNumber++;

                    nextLinkPairs.Add((match1, nextMatch));
                    nextLinkPairs.Add((match2, nextMatch));

                    allMatches.Add(nextMatch);
                    nextRoundMatches.Add(nextMatch);
                }

                // Capture Semi-Finals BEFORE moving to next round
                if (roundName == "Semi-Finals")
                {
                    semiFinalMatches = nextRoundMatches.ToList();
                }

                currentRoundMatches = nextRoundMatches;
                teamsInRound /= 2;
                roundNumber++;
            }

            // 4. Create 3rd Place Match AFTER Semi-Finals (if enabled and Semi-Finals exist)
            if (hasThirdPlaceMatch && semiFinalMatches != null && semiFinalMatches.Count == 2)
            {
                var thirdPlaceMatch = new Match
                {
                    ScheduleId = scheduleId,
                    RoundName = "3rd Place",
                    RoundNumber = roundNumber,
                    MatchNumber = 1,
                    Status = MatchStatus.Active,
                    MatchPosition = null,
                    IsThirdPlaceMatch = true // *** ADD THIS LINE ***
                };
                allMatches.Add(thirdPlaceMatch);

                // Link losers of Semi-Finals to 3rd place match
                loserLinkPairs.Add((semiFinalMatches[0], thirdPlaceMatch));
                loserLinkPairs.Add((semiFinalMatches[1], thirdPlaceMatch));
            }

            // 5. Save all matches to the database
            await _context.Matches.AddRangeAsync(allMatches);
            await _context.SaveChangesAsync();

            // 5b. Update NextMatchId / NextLoserMatchId
            foreach (var (child, parent) in nextLinkPairs)
            {
                child.NextMatchId = parent.MatchId;
                _context.Matches.Update(child);
            }
            foreach (var (child, parent) in loserLinkPairs)
            {
                child.NextLoserMatchId = parent.MatchId;
                _context.Matches.Update(child);
            }

            if (nextLinkPairs.Any() || loserLinkPairs.Any())
            {
                await _context.SaveChangesAsync();
            }

            // 6. Auto-advance BYEs
            var byeMatches = allMatches.Where(m => m.IsBye).ToList();
            if (byeMatches.Any())
            {
                foreach (var byeMatch in byeMatches)
                {
                    await AdvanceWinner(byeMatch);
                }
                await _context.SaveChangesAsync();
            }
        }

        private string GetRoundName(int teamsInRound)
        {
            if (teamsInRound == 2) return "Final";
            if (teamsInRound == 4) return "Semi-Finals";
            if (teamsInRound == 8) return "Quarter-Finals";
            return $"Round of {teamsInRound}";
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
                .Include(s => s.Participants)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            var matches = await _context.Matches
                .Where(m => m.ScheduleId == id)
                .Include(m => m.Team1)
                .Include(m => m.Team2)
                // --- ADD THIS INCLUDE ---
                .Include(m => m.LastUpdatedByUser) // To get the username for "Saved By"
                                                   // --- END ADD ---
                .OrderBy(m => m.RoundNumber)
                .ThenBy(m => m.RoundName)
                .ThenBy(m => m.MatchNumber)
                .ToListAsync();

            int? currentUserId = GetCurrentUserId();
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
                IsOrganizer = isOrganizer
            };

            return PartialView("~/Views/Competition/_MatchListing.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMatch(int MatchId, string Team1Score, string Team2Score)
        {
            var match = await _context.Matches.FindAsync(MatchId);
            if (match == null) return NotFound();

            match.Team1Score = Team1Score;
            match.Team2Score = Team2Score;
            match.LastUpdatedByUserId = GetCurrentUserId();
            match.WinnerId = DetermineWinnerId(match.Team1Id, match.Team2Id, Team1Score, Team2Score);

            bool hasRealScores = Team1Score.Split(',').Any(s => s.Trim() != "0" && !string.IsNullOrEmpty(s.Trim())) ||
                                 Team2Score.Split(',').Any(s => s.Trim() != "0" && !string.IsNullOrEmpty(s.Trim()));

            if (hasRealScores && match.Status != MatchStatus.Bye)
            {
                match.Status = MatchStatus.Done;

                // --- NEW: ADVANCE WINNER ---
                await AdvanceWinner(match);
                // --- END NEW ---
            }

            _context.Matches.Update(match);
            await _context.SaveChangesAsync();

            return RedirectToAction("CompDetails", "Competition", new { id = match.ScheduleId, tab = "match-listing" });
        }

        // --- *** THIS IS THE NEW HELPER METHOD *** ---
        private async Task AdvanceWinner(Match completedMatch)
        {
            if (completedMatch.WinnerId == null) return; // No winner

            var winnerId = completedMatch.WinnerId;
            var loserId = (completedMatch.Team1Id == winnerId) ? completedMatch.Team2Id : completedMatch.Team1Id;

            // 1. Advance the WINNER
            if (completedMatch.NextMatchId.HasValue)
            {
                var nextMatch = await _context.Matches.FindAsync(completedMatch.NextMatchId.Value);
                if (nextMatch != null)
                {
                    // Place winner in the correct slot (1 or 2)
                    if (completedMatch.MatchPosition == 1)
                    {
                        nextMatch.Team1Id = winnerId;
                    }
                    else
                    {
                        nextMatch.Team2Id = winnerId;
                    }
                    _context.Matches.Update(nextMatch);
                }
            }

            // 2. Advance the LOSER (for 3rd place match)
            if (completedMatch.NextLoserMatchId.HasValue && loserId.HasValue)
            {
                var loserMatch = await _context.Matches.FindAsync(completedMatch.NextLoserMatchId.Value);
                if (loserMatch != null)
                {
                    // Place loser in the correct slot (1 or 2)
                    if (completedMatch.MatchPosition == 1)
                    {
                        loserMatch.Team1Id = loserId;
                    }
                    else
                    {
                        loserMatch.Team2Id = loserId;
                    }
                    _context.Matches.Update(loserMatch);
                }
            }
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
        [HttpGet]
public async Task<IActionResult> GetBracketData(int scheduleId)
{
    var competition = await _context.Competitions
        .FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);

    if (competition == null)
    {
        return NotFound(new { message = "Competition not found" });
    }

    // Check if competition has started (matches exist)
    var hasMatches = await _context.Matches
        .AnyAsync(m => m.ScheduleId == scheduleId);

    if (hasMatches)
    {
        // Competition started - return DYNAMIC bracket based on match results
        return await GetDynamicBracketData(scheduleId, competition);
    }
    else
    {
        // Competition NOT started - return STATIC bracket based on seeds
        return await GetStaticBracketData(scheduleId, competition);
    }
}

// New helper method for static bracket (before competition starts)
private async Task<IActionResult> GetStaticBracketData(int scheduleId, Competition competition)
{
    var teams = await _context.Teams
        .Where(t => t.ScheduleId == scheduleId && 
                   t.Status == TeamStatus.Confirmed && 
                   t.BracketSeed.HasValue)
        .OrderBy(t => t.BracketSeed)
        .ToListAsync();

    if (!teams.Any())
    {
        return Json(new
        {
            bracketData = new { teams = new string[0][], results = new object[0][] },
            roundHeaders = new string[0],
            hasThirdPlaceMatch = competition.ThirdPlaceMatch,
            thirdPlace = (object)null!
        });
    }

    // Calculate bracket size
    int bracketSize;
    if (teams.Count <= 8)
        bracketSize = 8;
    else
        bracketSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(teams.Count, 2)));
    
    if (bracketSize > 64) bracketSize = 64;

    // Standard seeding orders
    Dictionary<int, int[]> seeding = new Dictionary<int, int[]>
    {
        { 8, new[] { 1, 8, 4, 5, 3, 6, 2, 7 } },
        { 16, new[] { 1, 16, 8, 9, 5, 12, 4, 13, 6, 11, 3, 14, 7, 10, 2, 15 } },
        { 32, new[] { 1, 32, 17, 16, 9, 24, 25, 8, 5, 28, 21, 12, 13, 20, 
                29, 4, 3, 30, 19, 14, 11, 22, 27, 6, 7, 26, 23, 10, 15, 18, 31, 2 } },
        { 64, new int[64] }
    };

    var seeds = seeding.GetValueOrDefault(bracketSize) ?? new int[bracketSize];
    if (bracketSize == 64 && seeds[0] == 0)
    {
        for (int i = 0; i < 32; i++)
        {
            seeds[i * 2] = i + 1;
            seeds[i * 2 + 1] = 64 - i;
        }
    }

    // Create team lookup
    var teamLookup = teams.ToDictionary(t => t.BracketSeed!.Value, t => t);

    // Build team pairs
    var teamPairs = new List<string[]>();
    for (int i = 0; i < seeds.Length; i += 2)
    {
        var team1 = teamLookup.GetValueOrDefault(seeds[i]);
        var team2 = teamLookup.GetValueOrDefault(seeds[i + 1]);

        string team1Name = team1?.TeamName ?? 
                          (seeds[i] <= teams.Count ? $"Seed {seeds[i]}" : "BYE");
        string team2Name = team2?.TeamName ?? 
                          (seeds[i + 1] <= teams.Count ? $"Seed {seeds[i + 1]}" : "BYE");

        teamPairs.Add(new[] { team1Name, team2Name });
    }

    // Create empty results for first round
    var firstRoundResults = new List<int[]>();
    for (int i = 0; i < teamPairs.Count; i++)
    {
        firstRoundResults.Add(new int[0]); // Empty array = no scores yet
    }

    // Build round headers
    int rounds = (int)Math.Log(bracketSize, 2);
    var roundHeaders = new List<string>();
    for (int r = 0; r < rounds; r++)
    {
        int teamsInRound = (int)Math.Pow(2, rounds - r);
        if (teamsInRound == 2) roundHeaders.Add("Final");
        else if (teamsInRound == 4) roundHeaders.Add("Semi-Finals");
        else if (teamsInRound == 8) roundHeaders.Add("Quarter-Finals");
        else roundHeaders.Add("Round of " + teamsInRound);
    }

    var bracketData = new
    {
        teams = teamPairs,
        results = new[] { firstRoundResults }
    };

    // Third place (not applicable before matches start)
    object thirdPlace = null!;
    if (competition.ThirdPlaceMatch)
    {
        thirdPlace = new
        {
            team1 = "TBD",
            team2 = "TBD",
            status = "Pending",
            winnerIndex = (int?)null
        };
    }

    return Json(new
    {
        bracketData,
        roundHeaders,
        hasThirdPlaceMatch = competition.ThirdPlaceMatch,
        thirdPlace
    });
}

// Helper method for dynamic bracket (after competition starts)
private async Task<IActionResult> GetDynamicBracketData(int scheduleId, Competition competition)
{
    // Load all elimination matches
    var matches = await _context.Matches
        .Where(m => m.ScheduleId == scheduleId)
        .Include(m => m.Team1)
        .Include(m => m.Team2)
        .OrderBy(m => m.RoundNumber)
        .ThenBy(m => m.MatchNumber)
        .ToListAsync();

    if (!matches.Any())
    {
        return Json(new
        {
            bracketData = new { teams = new string[0][], results = new object[0][] },
            roundHeaders = new string[0],
            hasThirdPlaceMatch = competition.ThirdPlaceMatch,
            thirdPlace = (object)null!
        });
    }

    // Group matches by round
    var roundGroups = matches
        .Where(m => !m.IsThirdPlaceMatch)
        .GroupBy(m => m.RoundNumber)
        .OrderBy(g => g.Key)
        .ToList();

    // Build team pairs from first round
    var firstRound = roundGroups.FirstOrDefault()?.ToList() ?? new List<Match>();
    var teamPairs = firstRound.Select(m => new[]
    {
        m.Team1?.TeamName ?? "TBD",
        m.Team2?.TeamName ?? "TBD"
    }).ToList();

    // Build results for all rounds
    var allResults = new List<List<int[]>>();
    foreach (var round in roundGroups)
    {
        var roundResults = round.Select(m =>
        {
            if (m.Status == MatchStatus.Done && m.WinnerId.HasValue)
            {
                // Parse scores if available
                if (!string.IsNullOrEmpty(m.Team1Score) && !string.IsNullOrEmpty(m.Team2Score))
                {
                    var team1Sets = m.Team1Score.Split(',')
                        .Select(s => int.TryParse(s.Trim(), out var val) ? val : 0)
                        .ToArray();
                    var team2Sets = m.Team2Score.Split(',')
                        .Select(s => int.TryParse(s.Trim(), out var val) ? val : 0)
                        .ToArray();
                    
                    // Count set wins for bracket display
                    int team1Wins = 0;
                    int team2Wins = 0;
                    for (int i = 0; i < Math.Min(team1Sets.Length, team2Sets.Length); i++)
                    {
                        if (team1Sets[i] > team2Sets[i]) team1Wins++;
                        else if (team2Sets[i] > team1Sets[i]) team2Wins++;
                    }
                    
                    return new[] { team1Wins, team2Wins };
                }
                // Or just indicate winner
                return m.WinnerId == m.Team1Id ? new[] { 1, 0 } : new[] { 0, 1 };
            }
            return new int[0]; // Empty = match not completed
        }).ToList();
        
        allResults.Add(roundResults);
    }

    // Build round headers
    var roundHeaders = new List<string>();
    int totalRounds = roundGroups.Count;
    for (int i = 0; i < totalRounds; i++)
    {
        int teamsInRound = (int)Math.Pow(2, totalRounds - i);
        if (teamsInRound == 2) roundHeaders.Add("Final");
        else if (teamsInRound == 4) roundHeaders.Add("Semi-Finals");
        else if (teamsInRound == 8) roundHeaders.Add("Quarter-Finals");
        else roundHeaders.Add("Round of " + teamsInRound);
    }

    var bracketData = new
    {
        teams = teamPairs,
        results = allResults
    };

    // Handle third place match
    object thirdPlace = null!;
    if (competition.ThirdPlaceMatch)
    {
        var thirdPlaceMatch = matches.FirstOrDefault(m => m.IsThirdPlaceMatch);
        if (thirdPlaceMatch != null)
        {
            thirdPlace = new
            {
                team1 = thirdPlaceMatch.Team1?.TeamName ?? "TBD",
                team2 = thirdPlaceMatch.Team2?.TeamName ?? "TBD",
                status = thirdPlaceMatch.Status.ToString(),
                winnerIndex = thirdPlaceMatch.WinnerId.HasValue
                    ? (thirdPlaceMatch.WinnerId == thirdPlaceMatch.Team1Id ? 0 : 1)
                    : (int?)null
            };
        }
        else
        {
            thirdPlace = new
            {
                team1 = "TBD",
                team2 = "TBD",
                status = "Pending",
                winnerIndex = (int?)null
            };
        }
    }

    return Json(new
    {
        bracketData,
        roundHeaders,
        hasThirdPlaceMatch = competition.ThirdPlaceMatch,
        thirdPlace
    });
}
    }
}