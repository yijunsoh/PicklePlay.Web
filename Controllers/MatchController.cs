using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PicklePlay.Application.Services;

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

    // Clear any old matches if they exist
    var oldMatches = await _context.Matches.Where(m => m.ScheduleId == id).ToListAsync();
    if (oldMatches.Any())
    {
        _context.Matches.RemoveRange(oldMatches);
        await _context.SaveChangesAsync();
    }

    // *** FIX: Properly save matches based on format ***
    switch (competition.Format)
    {
        case CompetitionFormat.PoolPlay:
            // Generate pool matches + empty playoff bracket
            var poolMatches = await GeneratePoolPlayMatchesWithPlayoff(id, competition);
            // *** FIX: Actually save the matches ***
    await _context.Matches.AddRangeAsync(poolMatches);
    await _context.SaveChangesAsync(); // Save here!
    
    Console.WriteLine($"Saved {poolMatches.Count} Pool Play matches (including playoff structure)");
    break;
            
        case CompetitionFormat.Elimination:
            // This function saves matches directly to DB
            await GenerateEliminationBracket(confirmedTeams, id, competition.ThirdPlaceMatch);
            break;
            
        case CompetitionFormat.RoundRobin:
            var rrMatches = GenerateRoundRobinMatches(confirmedTeams, id);
            await _context.Matches.AddRangeAsync(rrMatches);
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
    Console.WriteLine($"GetMatchListing called with id: {id}");
    
    var competition = await _context.Competitions
        .FirstOrDefaultAsync(c => c.ScheduleId == id);

    if (competition == null)
    {
        Console.WriteLine("Competition not found");
        return PartialView("~/Views/Competition/_MatchListingEmpty.cshtml");
    }

    Console.WriteLine($"Competition found: Format = {competition.Format}");

    // *** ADD: Check if current user is organizer ***
    var currentUserId = GetCurrentUserId();
    bool isOrganizer = false;
    
    if (currentUserId.HasValue)
    {
        isOrganizer = await _context.ScheduleParticipants
            .AnyAsync(p => p.ScheduleId == id && 
                          p.UserId == currentUserId.Value && 
                          p.Role == ParticipantRole.Organizer);
    }

    if (competition.Format == CompetitionFormat.PoolPlay)
    {
        return await GetPoolPlayMatchListing(id, competition, isOrganizer); // *** PASS isOrganizer ***
    }
    else
    {
        // Existing elimination logic
        var matches = await _context.Matches
            .Where(m => m.ScheduleId == id)
            .Include(m => m.Team1)
            .Include(m => m.Team2)
            .Include(m => m.LastUpdatedByUser) // *** ADD THIS ***
            .OrderBy(m => m.RoundNumber)
            .ThenBy(m => m.MatchNumber)
            .ToListAsync();

        Console.WriteLine($"Elimination matches found: {matches.Count}");

        var viewModel = new MatchListingViewModel
        {
            ScheduleId = id,
            Matches = matches,
            IsOrganizer = isOrganizer // *** ADD THIS ***
        };

        return PartialView("~/Views/Competition/_MatchListing.cshtml", viewModel);
    }
}

        private async Task<IActionResult> GetPoolPlayMatchListing(int scheduleId, Competition competition, bool isOrganizer)
{
    Console.WriteLine($"GetPoolPlayMatchListing called for schedule: {scheduleId}");
    
    var matches = await _context.Matches
        .Where(m => m.ScheduleId == scheduleId)
        .Include(m => m.Team1)
        .Include(m => m.Team2)
        .Include(m => m.LastUpdatedByUser) // *** ADD THIS ***
        .OrderBy(m => m.RoundNumber)
        .ThenBy(m => m.MatchNumber)
        .ToListAsync();

    Console.WriteLine($"Total matches found: {matches.Count}");

    var poolMatches = matches.Where(m => m.RoundNumber == 1).ToList();
    var playoffMatches = matches.Where(m => m.RoundNumber >= 2).ToList();

    Console.WriteLine($"Pool matches: {poolMatches.Count}, Playoff matches: {playoffMatches.Count}");

    var poolGroups = poolMatches
        .GroupBy(m => m.RoundName)
        .Select(g => new PoolMatchGroup
        {
            PoolName = g.Key!,
            Matches = g.ToList(),
            IsComplete = g.All(m => m.Status == MatchStatus.Done)
        })
        .OrderBy(g => g.PoolName)
        .ToList();

    Console.WriteLine($"Pool groups created: {poolGroups.Count}");

    bool allPoolsComplete = poolGroups.Any() && poolGroups.All(g => g.IsComplete);
    bool playoffStarted = playoffMatches.Any(m => m.Status != MatchStatus.Pending);

    var viewModel = new PoolPlayMatchListingViewModel
    {
        ScheduleId = scheduleId,
        PoolGroups = poolGroups,
        PlayoffGroup = new PlayoffMatchGroup
        {
            Matches = playoffMatches,
            HasStarted = playoffStarted,
            Status = playoffStarted ? "Active" : (allPoolsComplete ? "Ready" : "Pending")
        },
        IsPoolStageComplete = allPoolsComplete,
        CanAdvanceToPlayoff = allPoolsComplete && !playoffStarted,
        IsOrganizer = isOrganizer // *** ADD THIS ***
    };

    Console.WriteLine($"Returning view with {viewModel.PoolGroups.Count} pool groups");

    return PartialView("~/Views/Competition/_PoolPlayMatchListing.cshtml", viewModel);
}


        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateMatch(int MatchId, string Team1Score, string Team2Score)
{
    try
    {
        Console.WriteLine($"=== UpdateMatch called ===");
        Console.WriteLine($"MatchId: {MatchId}");
        Console.WriteLine($"Team1Score: {Team1Score}");
        Console.WriteLine($"Team2Score: {Team2Score}");
        
        var match = await _context.Matches.FindAsync(MatchId);
        if (match == null)
        {
            Console.WriteLine("ERROR: Match not found");
            return NotFound();
        }
        
        Console.WriteLine($"Match found - Team1Id: {match.Team1Id}, Team2Id: {match.Team2Id}");
        
        match.Team1Score = Team1Score;
        match.Team2Score = Team2Score;
        
        // Get current user ID
        var currentUserId = GetCurrentUserId();
        Console.WriteLine($"Current UserId: {currentUserId}");
        
        if (currentUserId.HasValue)
        {
            match.LastUpdatedByUserId = currentUserId.Value;
        }
        else
        {
            Console.WriteLine("WARNING: No current user ID");
        }
        
        // Determine winner
        Console.WriteLine("Calling DetermineWinnerId...");
        match.WinnerId = DetermineWinnerId(match.Team1Id, match.Team2Id, Team1Score, Team2Score);
        Console.WriteLine($"WinnerId determined: {match.WinnerId}");

        // Check if there are real scores
        bool hasRealScores = false;
        if (!string.IsNullOrEmpty(Team1Score) && !string.IsNullOrEmpty(Team2Score))
        {
            hasRealScores = Team1Score.Split(',').Any(s => s.Trim() != "0" && !string.IsNullOrEmpty(s.Trim())) ||
                           Team2Score.Split(',').Any(s => s.Trim() != "0" && !string.IsNullOrEmpty(s.Trim()));
        }
        
        Console.WriteLine($"Has real scores: {hasRealScores}");

        if (hasRealScores && match.Status != MatchStatus.Bye)
        {
            match.Status = MatchStatus.Done;
            Console.WriteLine("Status set to Done, calling AdvanceWinner...");
            await AdvanceWinner(match);
        }

        Console.WriteLine("Updating match in database...");
        _context.Matches.Update(match);
        await _context.SaveChangesAsync();
        
        Console.WriteLine("=== UpdateMatch completed successfully ===");

        return RedirectToAction("CompDetails", "Competition", new { id = match.ScheduleId, tab = "match-listing" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"=== ERROR in UpdateMatch ===");
        Console.WriteLine($"Exception Type: {ex.GetType().Name}");
        Console.WriteLine($"Message: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        
        if (ex.InnerException != null)
        {
            Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
        }
        
        throw; // Re-throw to see in debugger
    }
}

private int? DetermineWinnerId(int? team1Id, int? team2Id, string team1Score, string team2Score)
{
    try
    {
        Console.WriteLine($"=== DetermineWinnerId ===");
        Console.WriteLine($"Team1Id: {team1Id}, Team2Id: {team2Id}");
        Console.WriteLine($"Team1Score: '{team1Score}', Team2Score: '{team2Score}'");
        
        // Handle null/empty scores
        if (string.IsNullOrWhiteSpace(team1Score) || string.IsNullOrWhiteSpace(team2Score))
        {
            Console.WriteLine("Scores are null/empty, returning null");
            return null;
        }

        var setScores1 = team1Score.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var val) ? val : 0)
            .ToList();
        
        var setScores2 = team2Score.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var val) ? val : 0)
            .ToList();

        Console.WriteLine($"Parsed setScores1: {string.Join(", ", setScores1)}");
        Console.WriteLine($"Parsed setScores2: {string.Join(", ", setScores2)}");

        int team1SetWins = 0;
        int team2SetWins = 0;

        int sets = Math.Min(setScores1.Count, setScores2.Count);
        Console.WriteLine($"Number of sets: {sets}");

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

        Console.WriteLine($"Team1 set wins: {team1SetWins}, Team2 set wins: {team2SetWins}");

        if (team1SetWins > team2SetWins)
        {
            Console.WriteLine($"Team1 wins, returning {team1Id}");
            return team1Id;
        }
        if (team2SetWins > team1SetWins)
        {
            Console.WriteLine($"Team2 wins, returning {team2Id}");
            return team2Id;
        }

        Console.WriteLine("Draw or invalid scores, returning null");
        return null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in DetermineWinnerId: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        throw;
    }
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

[HttpGet]
public async Task<IActionResult> GetPlayoffBracketData(int scheduleId)
{
    // Get playoff matches only (RoundNumber >= 2)
    var playoffMatches = await _context.Matches
        .Where(m => m.ScheduleId == scheduleId && m.RoundNumber >= 2)
        .Include(m => m.Team1)
        .Include(m => m.Team2)
        .OrderBy(m => m.RoundNumber)
        .ThenBy(m => m.MatchNumber)
        .ToListAsync();

    if (!playoffMatches.Any())
    {
        return Json(new
        {
            bracketData = new { teams = new string[0][], results = new object[0][] },
            roundHeaders = new string[0],
            hasThirdPlaceMatch = false,
            thirdPlace = (object)null!
        });
    }

    // Group matches by round (excluding third place)
    var roundGroups = playoffMatches
        .Where(m => !m.IsThirdPlaceMatch)
        .GroupBy(m => m.RoundNumber)
        .OrderBy(g => g.Key)
        .ToList();

    // Build team pairs from first playoff round
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
                // Return 1 for winner, 0 for loser
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
    var thirdPlaceMatch = playoffMatches.FirstOrDefault(m => m.IsThirdPlaceMatch);
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

    return Json(new
    {
        bracketData,
        roundHeaders,
        hasThirdPlaceMatch = thirdPlaceMatch != null,
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

// Add this method to generate Pool Play matches with playoff preparation
private async Task<List<Match>> GeneratePoolPlayMatchesWithPlayoff(int scheduleId, Competition competition)
{
    var matches = new List<Match>();
    
    // Load all pools with teams
    var pools = await _context.Pools
        .Where(p => p.ScheduleId == scheduleId)
        .ToListAsync();

    // Load all confirmed teams for this schedule
    var allTeams = await _context.Teams
        .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
        .ToListAsync();

    // STEP 1: Generate Round Robin matches within each pool
    foreach (var pool in pools)
    {
        var teamsInPool = allTeams.Where(t => t.PoolId == pool.PoolId).ToList();
        
        if (teamsInPool.Count < 2)
        {
            continue; // Skip pool if less than 2 teams
        }

        int matchNumber = 1;
        
        // Round Robin: Each team plays every other team once
        for (int i = 0; i < teamsInPool.Count; i++)
        {
            for (int j = i + 1; j < teamsInPool.Count; j++)
            {
                matches.Add(new Match
                {
                    ScheduleId = scheduleId,
                    Team1Id = teamsInPool[i].TeamId,
                    Team2Id = teamsInPool[j].TeamId,
                    RoundName = pool.PoolName, // e.g., "Pool A"
                    RoundNumber = 1, // Pool stage is round 1
                    MatchNumber = matchNumber++,
                    Status = MatchStatus.Active,
                    IsThirdPlaceMatch = false
                });
            }
        }
    }

    // STEP 2: Calculate how many teams will advance
    int totalAdvancingTeams = pools.Count * competition.WinnersPerPool;
    
    if (totalAdvancingTeams > 1)
    {
        // Determine playoff bracket size
        int playoffBracketSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(totalAdvancingTeams, 2)));
        
        Console.WriteLine($"Generating playoff bracket structure: {playoffBracketSize} slots");
        
        // Generate empty playoff matches
        int matchNumber = matches.Count + 1;
        int roundNumber = 2; // Start from round 2 (pool stage is round 1)
        int totalRounds = (int)Math.Log(playoffBracketSize, 2);
        
        var currentRoundMatches = new List<Match>();
        
        // Generate first playoff round
        for (int i = 0; i < playoffBracketSize / 2; i++)
        {
            string roundName = GetPlayoffRoundName(playoffBracketSize, 1);
            
            var match = new Match
            {
                ScheduleId = scheduleId,
                Team1Id = null, // TBD
                Team2Id = null, // TBD
                RoundName = roundName,
                RoundNumber = roundNumber,
                MatchNumber = matchNumber++,
                Status = MatchStatus.Pending,
                MatchPosition = (i % 2) + 1,
                IsThirdPlaceMatch = false
            };
            
            matches.Add(match);
            currentRoundMatches.Add(match);
        }
        
        // Generate subsequent rounds
        for (int round = 2; round <= totalRounds; round++)
        {
            roundNumber++;
            var nextRoundMatches = new List<Match>();
            int matchesInRound = (int)Math.Pow(2, totalRounds - round);
            
            string roundName = GetPlayoffRoundName(playoffBracketSize, round);
            
            for (int i = 0; i < matchesInRound; i++)
            {
                var match = new Match
                {
                    ScheduleId = scheduleId,
                    Team1Id = null,
                    Team2Id = null,
                    RoundName = roundName,
                    RoundNumber = roundNumber,
                    MatchNumber = matchNumber++,
                    Status = MatchStatus.Pending,
                    MatchPosition = (i % 2) + 1,
                    IsThirdPlaceMatch = false
                };
                
                matches.Add(match);
                nextRoundMatches.Add(match);
            }
            
            currentRoundMatches = nextRoundMatches;
        }
        
        // Add third place match if enabled
        if (competition.ThirdPlaceMatch && totalRounds >= 2)
        {
            var thirdPlaceMatch = new Match
            {
                ScheduleId = scheduleId,
                Team1Id = null,
                Team2Id = null,
                RoundName = "3rd Place Match",
                RoundNumber = roundNumber + 1,
                MatchNumber = matchNumber++,
                Status = MatchStatus.Pending,
                IsThirdPlaceMatch = true
            };
            matches.Add(thirdPlaceMatch);
        }
    }
    
    return matches;
}

// Helper method to generate empty playoff bracket
private List<Match> GenerateEmptyPlayoffBracket(int scheduleId, int bracketSize, bool hasThirdPlace)
{
    var playoffMatches = new List<Match>();
    int roundNumber = 2; // Start from round 2 (pool stage is round 1)
    int totalRounds = (int)Math.Log(bracketSize, 2);
    
    var currentRoundMatches = new List<Match>();
    
    // Generate first playoff round
    for (int i = 0; i < bracketSize / 2; i++)
    {
        string roundName = GetPlayoffRoundName(bracketSize, 1);
        
        var match = new Match
        {
            ScheduleId = scheduleId,
            Team1Id = null, // TBD - will be filled after pool stage
            Team2Id = null, // TBD - will be filled after pool stage
            RoundName = roundName,
            RoundNumber = roundNumber,
            MatchNumber = i + 1,
            Status = MatchStatus.Pending,
            MatchPosition = i,
            IsThirdPlaceMatch = false
        };
        
        playoffMatches.Add(match);
        currentRoundMatches.Add(match);
    }
    
    // Generate subsequent rounds
    for (int round = 2; round <= totalRounds; round++)
    {
        roundNumber++;
        var nextRoundMatches = new List<Match>();
        int matchesInRound = (int)Math.Pow(2, totalRounds - round);
        
        string roundName = GetPlayoffRoundName(bracketSize, round);
        
        for (int i = 0; i < matchesInRound; i++)
        {
            var match = new Match
            {
                ScheduleId = scheduleId,
                Team1Id = null,
                Team2Id = null,
                RoundName = roundName,
                RoundNumber = roundNumber,
                MatchNumber = i + 1,
                Status = MatchStatus.Pending,
                MatchPosition = i,
                IsThirdPlaceMatch = false
            };
            
            playoffMatches.Add(match);
            nextRoundMatches.Add(match);
            
            // Link previous round winners to this match
            if (i * 2 < currentRoundMatches.Count)
            {
                currentRoundMatches[i * 2].NextMatchId = match.MatchId;
            }
            if (i * 2 + 1 < currentRoundMatches.Count)
            {
                currentRoundMatches[i * 2 + 1].NextMatchId = match.MatchId;
            }
        }
        
        currentRoundMatches = nextRoundMatches;
    }
    
    // Add third place match if enabled
    if (hasThirdPlace && totalRounds >= 2)
    {
        var thirdPlaceMatch = new Match
        {
            ScheduleId = scheduleId,
            Team1Id = null,
            Team2Id = null,
            RoundName = "3rd Place",
            RoundNumber = roundNumber + 1,
            MatchNumber = 1,
            Status = MatchStatus.Pending,
            IsThirdPlaceMatch = true
        };
        playoffMatches.Add(thirdPlaceMatch);
    }
    
    return playoffMatches;
}

// Helper to get playoff round names
private string GetPlayoffRoundName(int bracketSize, int round)
{
    int totalRounds = (int)Math.Log(bracketSize, 2);
    int teamsInRound = (int)Math.Pow(2, totalRounds - round + 1);
    
    if (teamsInRound == 2) return "Final";
    if (teamsInRound == 4) return "Semi-Finals";
    if (teamsInRound == 8) return "Quarter-Finals";
    return $"Round of {teamsInRound}";
}

// REPLACE the entire AdvanceToPlayoff method

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> AdvanceToPlayoff(int scheduleId)
{
    try
    {
        Console.WriteLine($"=== AdvanceToPlayoff called for Schedule {scheduleId} ===");
        
        var competition = await _context.Competitions
            .FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);
        
        if (competition == null || competition.Format != CompetitionFormat.PoolPlay)
        {
            return Json(new { success = false, message = "Invalid competition or format." });
        }
        
        // Check if pool stage is complete
        var poolMatches = await _context.Matches
            .Where(m => m.ScheduleId == scheduleId && m.RoundNumber == 1)
            .ToListAsync();
        
        if (!poolMatches.Any() || !poolMatches.All(m => m.Status == MatchStatus.Done))
        {
            return Json(new { success = false, message = "All pool matches must be completed first." });
        }
        
        // *** CHANGED: Check if playoff teams are ALREADY ASSIGNED ***
        var playoffMatches = await _context.Matches
            .Where(m => m.ScheduleId == scheduleId && m.RoundNumber >= 2)
            .ToListAsync();
        
        if (!playoffMatches.Any())
        {
            return Json(new { success = false, message = "Playoff bracket structure not found. Please restart competition." });
        }
        
        // Check if teams are already assigned to first playoff round
        var firstPlayoffRound = playoffMatches
            .Where(m => m.RoundNumber == 2 && !m.IsThirdPlaceMatch)
            .ToList();
        
        if (firstPlayoffRound.Any(m => m.Team1Id.HasValue || m.Team2Id.HasValue))
        {
            return Json(new { success = false, message = "Playoff teams have already been assigned." });
        }
        
        // Get standings for each pool
        var pools = await _context.Pools
            .Where(p => p.ScheduleId == scheduleId)
            .Include(p => p.Teams)
            .ToListAsync();
        
        var qualifiedTeams = new List<(Team Team, string PoolName, int Position)>();
        
        foreach (var pool in pools.OrderBy(p => p.PoolName))
        {
            Console.WriteLine($"Processing pool: {pool.PoolName}");
            
            var poolStandings = new List<(Team Team, int Wins, int Losses, int Points)>();
            
            foreach (var team in pool.Teams)
            {
                var teamMatches = poolMatches
                    .Where(m => m.Team1Id == team.TeamId || m.Team2Id == team.TeamId)
                    .ToList();
                
                int wins = teamMatches.Count(m => m.WinnerId == team.TeamId);
                int losses = teamMatches.Count(m => 
                    m.WinnerId.HasValue && 
                    m.WinnerId != team.TeamId && 
                    (m.Team1Id == team.TeamId || m.Team2Id == team.TeamId));
                
                int points = (wins * 2) + losses; // 2 for win, 1 for loss
                
                poolStandings.Add((team, wins, losses, points));
                Console.WriteLine($"  {team.TeamName}: W={wins}, L={losses}, Pts={points}");
            }
            
            // Sort by Points DESC, then Wins DESC
            var sorted = poolStandings
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.Wins)
                .ToList();
            
            // Take top N teams per pool
            var topTeams = sorted.Take(competition.WinnersPerPool).ToList();
            
            for (int i = 0; i < topTeams.Count; i++)
            {
                qualifiedTeams.Add((topTeams[i].Team, pool.PoolName ?? "Unknown", i + 1));
                Console.WriteLine($"  Qualified: {topTeams[i].Team.TeamName} (Position {i + 1})");
            }
        }
        
        if (!qualifiedTeams.Any())
        {
            return Json(new { success = false, message = "No teams qualified for playoff." });
        }
        
        Console.WriteLine($"Total qualified teams: {qualifiedTeams.Count}");
        
        // *** SEED TEAMS: Alternate between pool positions ***
        // Example with 2 pools, 2 winners each:
        // Pool A #1, Pool B #1, Pool A #2, Pool B #2
        var seededTeams = new List<Team>();

// Group teams by position and pool
var poolGroups = qualifiedTeams
    .GroupBy(qt => qt.PoolName)
    .OrderBy(g => g.Key)
    .ToList();

int numPools = poolGroups.Count;
int winnersPerPool = competition.WinnersPerPool;

Console.WriteLine($"Seeding strategy: {numPools} pools x {winnersPerPool} winners each");

// Strategy: Alternate positions across pools to avoid same-pool matchups early
// Example with 2 pools, 2 winners each:
// Seed 1: Pool A #1
// Seed 2: Pool B #2
// Seed 3: Pool B #1
// Seed 4: Pool A #2
// This creates: Match 1 (A1 vs A2), Match 2 (B2 vs B1) - but we want cross-pool!

// BETTER STRATEGY: Snake/Zigzag seeding
if (numPools == 2 && winnersPerPool == 2)
{
    // Special case: 2 pools, 2 winners each
    // Desired matchups:
    // Match 1: Pool A #1 vs Pool B #2
    // Match 2: Pool B #1 vs Pool A #2
    
    var poolA = poolGroups[0].OrderBy(qt => qt.Position).ToList();
    var poolB = poolGroups[1].OrderBy(qt => qt.Position).ToList();
    
    seededTeams.Add(poolA[0].Team); // Pool A #1
    seededTeams.Add(poolB[1].Team); // Pool B #2
    seededTeams.Add(poolB[0].Team); // Pool B #1
    seededTeams.Add(poolA[1].Team); // Pool A #2
}
else
{
    // General case: Alternate top seeds, then lower seeds
    for (int pos = 1; pos <= winnersPerPool; pos++)
    {
        foreach (var poolGroup in poolGroups)
        {
            var teamAtPos = poolGroup.FirstOrDefault(qt => qt.Position == pos);
            if (teamAtPos.Team != null)
            {
                seededTeams.Add(teamAtPos.Team);
            }
        }
    }
}

Console.WriteLine("Seeded playoff teams (cross-pool strategy):");
for (int i = 0; i < seededTeams.Count; i++)
{
    Console.WriteLine($"  Seed {i + 1}: {seededTeams[i].TeamName}");
}
        
        // *** ASSIGN TEAMS TO EXISTING PLAYOFF MATCHES ***
        // Get first round playoff matches ordered by MatchNumber
        var firstRoundMatches = playoffMatches
            .Where(m => m.RoundNumber == 2 && !m.IsThirdPlaceMatch)
            .OrderBy(m => m.MatchNumber)
            .ToList();
        
        Console.WriteLine($"Assigning {seededTeams.Count} teams to {firstRoundMatches.Count} first-round matches");
        
        // Assign teams to matches (2 teams per match)
        for (int i = 0; i < firstRoundMatches.Count && i * 2 < seededTeams.Count; i++)
        {
            var match = firstRoundMatches[i];
            
            int team1Index = i * 2;
            int team2Index = i * 2 + 1;
            
            match.Team1Id = team1Index < seededTeams.Count ? seededTeams[team1Index].TeamId : null;
            match.Team2Id = team2Index < seededTeams.Count ? seededTeams[team2Index].TeamId : null;
            
            // Handle byes (if odd number of teams)
            if (!match.Team1Id.HasValue || !match.Team2Id.HasValue)
            {
                match.Status = MatchStatus.Bye;
                match.WinnerId = match.Team1Id ?? match.Team2Id;
                Console.WriteLine($"Match {match.MatchNumber}: BYE - Winner: {match.WinnerId}");
            }
            else
            {
                match.Status = MatchStatus.Active;
                Console.WriteLine($"Match {match.MatchNumber}: {match.Team1Id} vs {match.Team2Id}");
            }
            
            _context.Matches.Update(match);
        }
        
        await _context.SaveChangesAsync();
        Console.WriteLine("Teams assigned to playoff matches");
        
        // *** AUTO-ADVANCE BYE MATCHES ***
        var byeMatches = firstRoundMatches.Where(m => m.Status == MatchStatus.Bye).ToList();
        if (byeMatches.Any())
        {
            Console.WriteLine($"Auto-advancing {byeMatches.Count} bye matches");
            foreach (var byeMatch in byeMatches)
            {
                await AdvanceWinner(byeMatch);
            }
            await _context.SaveChangesAsync();
        }
        
        Console.WriteLine("=== AdvanceToPlayoff completed successfully ===");
        return Json(new { success = true, message = "Playoff bracket populated successfully!" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in AdvanceToPlayoff: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        return Json(new { success = false, message = $"Error: {ex.Message}" });
    }
}

 private async Task AdvanceWinner(Match completedMatch)
{
    try
    {
        Console.WriteLine($"=== AdvanceWinner called for Match {completedMatch.MatchId} ===");
        
        if (!completedMatch.WinnerId.HasValue)
        {
            Console.WriteLine("No winner determined, cannot advance");
            return;
        }

        var winnerId = completedMatch.WinnerId.Value;
        Console.WriteLine($"Winner ID: {winnerId}");

        // Handle regular match progression (NextMatchId)
        if (completedMatch.NextMatchId.HasValue)
        {
            var nextMatch = await _context.Matches.FindAsync(completedMatch.NextMatchId.Value);
            if (nextMatch != null)
            {
                Console.WriteLine($"Advancing to NextMatch {nextMatch.MatchId}");
                
                // Determine which position in the next match based on current match position
                if (completedMatch.MatchPosition == 1)
                {
                    nextMatch.Team1Id = winnerId;
                    Console.WriteLine($"Set Team1Id = {winnerId}");
                }
                else if (completedMatch.MatchPosition == 2)
                {
                    nextMatch.Team2Id = winnerId;
                    Console.WriteLine($"Set Team2Id = {winnerId}");
                }

                // If both teams are now set, activate the match
                if (nextMatch.Team1Id.HasValue && nextMatch.Team2Id.HasValue)
                {
                    nextMatch.Status = MatchStatus.Active;
                    Console.WriteLine($"NextMatch {nextMatch.MatchId} activated");
                }

                _context.Matches.Update(nextMatch);
            }
        }

        // Handle third place match (NextLoserMatchId)
        if (completedMatch.NextLoserMatchId.HasValue)
        {
            // Get the loser
            int? loserId = null;
            if (completedMatch.Team1Id.HasValue && completedMatch.Team2Id.HasValue)
            {
                loserId = (winnerId == completedMatch.Team1Id.Value) 
                    ? completedMatch.Team2Id.Value 
                    : completedMatch.Team1Id.Value;
            }

            if (loserId.HasValue)
            {
                var thirdPlaceMatch = await _context.Matches.FindAsync(completedMatch.NextLoserMatchId.Value);
                if (thirdPlaceMatch != null)
                {
                    Console.WriteLine($"Advancing loser {loserId} to 3rd Place Match {thirdPlaceMatch.MatchId}");
                    
                    // Assign loser to the third place match
                    if (thirdPlaceMatch.Team1Id == null)
                    {
                        thirdPlaceMatch.Team1Id = loserId.Value;
                        Console.WriteLine($"Set 3rd Place Team1Id = {loserId}");
                    }
                    else if (thirdPlaceMatch.Team2Id == null)
                    {
                        thirdPlaceMatch.Team2Id = loserId.Value;
                        Console.WriteLine($"Set 3rd Place Team2Id = {loserId}");
                    }

                    // If both teams are now set, activate the third place match
                    if (thirdPlaceMatch.Team1Id.HasValue && thirdPlaceMatch.Team2Id.HasValue)
                    {
                        thirdPlaceMatch.Status = MatchStatus.Active;
                        Console.WriteLine($"3rd Place Match {thirdPlaceMatch.MatchId} activated");
                    }

                    _context.Matches.Update(thirdPlaceMatch);
                }
            }
        }

        Console.WriteLine("=== AdvanceWinner completed ===");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in AdvanceWinner: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        throw;
    }
}


private async Task GeneratePlayoffBracket(List<Team> teams, int scheduleId, bool hasThirdPlaceMatch)
{
    Console.WriteLine($"=== GeneratePlayoffBracket - {teams.Count} teams ===");
    
    // Pad to next power of 2
    int nextPowerOf2 = (int)Math.Pow(2, Math.Ceiling(Math.Log(teams.Count) / Math.Log(2)));
    Console.WriteLine($"Bracket size (power of 2): {nextPowerOf2}");
    
    var allMatches = new List<Match>();
    int matchCounter = 1;
    int startingRoundNumber = 2; // Pool stage is RoundNumber 1
    
    // Determine number of rounds
    int totalRounds = (int)Math.Log(nextPowerOf2, 2);
    Console.WriteLine($"Total playoff rounds: {totalRounds}");
    
    // === STEP 1: CREATE ALL MATCHES FIRST (without linking) ===
    var roundMatchLists = new List<List<Match>>();
    int teamsInRound = nextPowerOf2;
    
    for (int round = 0; round < totalRounds; round++)
    {
        string roundName = GetRoundName(teamsInRound);
        int roundNumber = startingRoundNumber + round;
        var roundMatches = new List<Match>();
        
        Console.WriteLine($"Creating Round {roundNumber}: {roundName} ({teamsInRound} teams)");
        
        for (int i = 0; i < teamsInRound / 2; i++)
        {
            var match = new Match
            {
                ScheduleId = scheduleId,
                MatchNumber = matchCounter++,
                RoundNumber = roundNumber,
                RoundName = roundName,
                MatchPosition = (i % 2) + 1, // Alternate 1, 2, 1, 2...
                Status = MatchStatus.Pending,
                IsThirdPlaceMatch = false
            };
            
            // Only assign teams to FIRST round
            if (round == 0)
            {
                int team1Index = i * 2;
                int team2Index = i * 2 + 1;
                
                match.Team1Id = team1Index < teams.Count ? teams[team1Index].TeamId : null;
                match.Team2Id = team2Index < teams.Count ? teams[team2Index].TeamId : null;
                
                // Handle byes
                if (!match.Team1Id.HasValue || !match.Team2Id.HasValue)
                {
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = match.Team1Id ?? match.Team2Id;
                }
                else
                {
                    match.Status = MatchStatus.Active;
                }
                
                Console.WriteLine($"  Match {match.MatchNumber}: Team1={match.Team1Id}, Team2={match.Team2Id}, Status={match.Status}");
            }
            
            roundMatches.Add(match);
            allMatches.Add(match);
        }
        
        roundMatchLists.Add(roundMatches);
        teamsInRound /= 2;
    }
    
    // Add third place match if needed
    Match? thirdPlaceMatch = null;
    if (hasThirdPlaceMatch && totalRounds >= 2)
    {
        thirdPlaceMatch = new Match
        {
            ScheduleId = scheduleId,
            MatchNumber = matchCounter++,
            RoundNumber = startingRoundNumber + totalRounds,
            RoundName = "3rd Place Match",
            Status = MatchStatus.Pending,
            IsThirdPlaceMatch = true
        };
        allMatches.Add(thirdPlaceMatch);
        Console.WriteLine($"Created 3rd Place Match");
    }
    
    // === STEP 2: SAVE ALL MATCHES TO GET IDs ===
    await _context.Matches.AddRangeAsync(allMatches);
    await _context.SaveChangesAsync();
    Console.WriteLine($"Saved {allMatches.Count} matches to database");
    
    // === STEP 3: LINK MATCHES (NextMatchId, MatchPosition) ===
    for (int round = 0; round < roundMatchLists.Count - 1; round++)
    {
        var currentRound = roundMatchLists[round];
        var nextRound = roundMatchLists[round + 1];
        
        for (int i = 0; i < currentRound.Count; i++)
        {
            int nextMatchIndex = i / 2;
            if (nextMatchIndex < nextRound.Count)
            {
                currentRound[i].NextMatchId = nextRound[nextMatchIndex].MatchId;
                currentRound[i].MatchPosition = (i % 2 == 0) ? 1 : 2;
                
                Console.WriteLine($"Linked Match {currentRound[i].MatchId} → Match {nextRound[nextMatchIndex].MatchId} (Position {currentRound[i].MatchPosition})");
            }
        }
    }
    
    // Link Semi-Finals to 3rd Place Match (losers)
    if (thirdPlaceMatch != null && roundMatchLists.Count >= 2)
    {
        var semiFinals = roundMatchLists[roundMatchLists.Count - 2]; // Second-to-last round
        
        if (semiFinals.Count == 2)
        {
            semiFinals[0].NextLoserMatchId = thirdPlaceMatch.MatchId;
            semiFinals[1].NextLoserMatchId = thirdPlaceMatch.MatchId;
            
            Console.WriteLine($"Linked Semi-Finals losers to 3rd Place Match {thirdPlaceMatch.MatchId}");
        }
    }
    
    // === STEP 4: UPDATE ALL LINKS ===
    _context.Matches.UpdateRange(allMatches);
    await _context.SaveChangesAsync();
    Console.WriteLine("Updated all match links");
    
    // === STEP 5: AUTO-ADVANCE BYE MATCHES ===
    var byeMatches = allMatches.Where(m => m.Status == MatchStatus.Bye).ToList();
    if (byeMatches.Any())
    {
        Console.WriteLine($"Auto-advancing {byeMatches.Count} bye matches");
        foreach (var byeMatch in byeMatches)
        {
            await AdvanceWinner(byeMatch);
        }
        await _context.SaveChangesAsync();
    }
    
    Console.WriteLine("=== Playoff bracket generation completed ===");
}

    }
}