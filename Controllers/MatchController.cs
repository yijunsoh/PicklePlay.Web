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


       [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> StartCompetition(int id)
{
    var schedule = await _context.Schedules
        .Include(s => s.Participants)
        .FirstOrDefaultAsync(s => s.ScheduleId == id);
    
    var competition = await _context.Competitions
        .FirstOrDefaultAsync(c => c.ScheduleId == id);

    if (schedule == null || competition == null) return NotFound();

    // *** VALIDATION 1-4 (keep as is) ***
    var currentUserId = GetCurrentUserId();
    if (!currentUserId.HasValue)
    {
        TempData["ErrorMessage"] = "Please log in to start the competition.";
        return RedirectToAction("CompDetails", "Competition", new { id = id });
    }

    var isOrganizer = schedule.Participants
        .Any(p => p.UserId == currentUserId.Value && 
                 (p.Role == ParticipantRole.Organizer || p.Role == ParticipantRole.Staff));

    if (!isOrganizer)
    {
        TempData["ErrorMessage"] = "Only organizers or staff can start the competition.";
        return RedirectToAction("CompDetails", "Competition", new { id = id });
    }

    if (competition.Format != CompetitionFormat.RoundRobin && !competition.DrawPublished)
    {
        TempData["ErrorMessage"] = "Please publish the draw before starting the competition.";
        return RedirectToAction("CompDetails", "Competition", new { id = id });
    }

    var hasAwardSettings = await _context.Awards
        .AnyAsync(a => a.ScheduleId == id);

    if (!hasAwardSettings)
    {
        TempData["ErrorMessage"] = "Please configure award settings in the Actions dropdown before starting the competition.";
        return RedirectToAction("CompDetails", "Competition", new { id = id });
    }

    var confirmedTeams = await _context.Teams
        .Where(t => t.ScheduleId == id && t.Status == TeamStatus.Confirmed)
        .ToListAsync();

    if (!confirmedTeams.Any())
    {
        TempData["ErrorMessage"] = "No confirmed teams found. Please add teams before starting.";
        return RedirectToAction("CompDetails", "Competition", new { id = id });
    }

    // Clear any old matches if they exist
    var oldMatches = await _context.Matches
        .Where(m => m.ScheduleId == id)
        .ToListAsync();

    bool hasExistingEliminationBracket =
        competition.Format == CompetitionFormat.Elimination &&
        oldMatches.Any();

    if (!hasExistingEliminationBracket && oldMatches.Any())
    {
        _context.Matches.RemoveRange(oldMatches);
        await _context.SaveChangesAsync();
    }

    // *** FIX: Generate matches based on format ***
    switch (competition.Format)
    {
        case CompetitionFormat.PoolPlay:
            // ⬇️ CHANGED: This method now saves internally
            await GeneratePoolPlayMatchesWithPlayoff(id, competition);
            Console.WriteLine("Pool Play matches generated and saved");
            break;

        case CompetitionFormat.Elimination:
            if (hasExistingEliminationBracket)
            {
                Console.WriteLine("Elimination bracket already exists. Skipping regeneration.");
            }
            else
            {
                await GenerateEliminationBracket(confirmedTeams, id, competition.ThirdPlaceMatch);
            }
            break;

        case CompetitionFormat.RoundRobin:
            var rrMatches = GenerateRoundRobinMatches(confirmedTeams, id);
            await _context.Matches.AddRangeAsync(rrMatches);
            await _context.SaveChangesAsync();
            break;
    }

    // Update schedule status to "In Progress"
    schedule.Status = ScheduleStatus.InProgress;
    _context.Schedules.Update(schedule);

    await _context.SaveChangesAsync();

    TempData["SuccessMessage"] = "Competition Started! Match listing is now available.";
    return RedirectToAction("CompDetails", "Competition", new { id = id, tab = "match-listing" });
}

// ⬇️ CHANGE RETURN TYPE TO Task (void async)
private async Task GeneratePoolPlayMatchesWithPlayoff(int scheduleId, Competition competition)
{
    var matches = new List<Match>();

    // Load all pools with teams
    var pools = await _context.Pools
        .Where(p => p.ScheduleId == scheduleId)
        .ToListAsync();

    var allTeams = await _context.Teams
        .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
        .ToListAsync();

    // STEP 1: Generate Round Robin matches within each pool
    foreach (var pool in pools)
    {
        var teamsInPool = allTeams.Where(t => t.PoolId == pool.PoolId).ToList();

        if (teamsInPool.Count < 2)
        {
            continue;
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
                    Status = MatchStatus.Active,
                    IsThirdPlaceMatch = false
                });
            }
        }
    }

    // STEP 2: Calculate playoff bracket
    int totalAdvancingTeams = pools.Count * competition.WinnersPerPool;

    if (totalAdvancingTeams > 1)
    {
        int playoffBracketSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(totalAdvancingTeams, 2)));

        Console.WriteLine($"Generating playoff bracket structure: {playoffBracketSize} slots");

        int matchNumber = matches.Count + 1;
        int roundNumber = 2;
        int totalRounds = (int)Math.Log(playoffBracketSize, 2);

        var allRounds = new List<List<Match>>();
        var currentRoundMatches = new List<Match>();

        // Generate first playoff round
        for (int i = 0; i < playoffBracketSize / 2; i++)
        {
            string roundName = GetPlayoffRoundName(playoffBracketSize, 1);

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
            currentRoundMatches.Add(match);
        }

        allRounds.Add(currentRoundMatches.ToList());

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

            allRounds.Add(nextRoundMatches.ToList());
        }

        // Save all matches to get IDs
        await _context.Matches.AddRangeAsync(matches);
        await _context.SaveChangesAsync();
        Console.WriteLine("Playoff structure saved, now linking matches...");

        // Link matches using NextMatchId
        for (int r = 0; r < allRounds.Count - 1; r++)
        {
            var currentRound = allRounds[r];
            var nextRound = allRounds[r + 1];

            for (int i = 0; i < currentRound.Count; i++)
            {
                var currentMatch = currentRound[i];
                int nextMatchIndex = i / 2;

                if (nextMatchIndex < nextRound.Count)
                {
                    currentMatch.NextMatchId = nextRound[nextMatchIndex].MatchId;
                    _context.Matches.Update(currentMatch);
                }
            }
        }

        await _context.SaveChangesAsync();
        Console.WriteLine("Match linking complete!");

        // Add third place match if enabled
        if (competition.ThirdPlaceMatch && totalRounds >= 2)
        {
            var semiFinals = allRounds[allRounds.Count - 2];

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

            await _context.Matches.AddAsync(thirdPlaceMatch);
            await _context.SaveChangesAsync();

            // Link semi-finals to third place match
            if (semiFinals.Count >= 2)
            {
                semiFinals[0].NextLoserMatchId = thirdPlaceMatch.MatchId;
                semiFinals[1].NextLoserMatchId = thirdPlaceMatch.MatchId;

                _context.Matches.UpdateRange(semiFinals);
                await _context.SaveChangesAsync();
            }
        }
    }
    else
    {
        // No playoff - just save pool matches
        await _context.Matches.AddRangeAsync(matches);
        await _context.SaveChangesAsync();
    }

    Console.WriteLine($"Total matches generated: {matches.Count}");
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

        private async Task GenerateEliminationBracket(List<Team> teams, int scheduleId, bool hasThirdPlace)
        {
if (teams == null || teams.Count < 2)
            {
                throw new InvalidOperationException("At least two confirmed teams are required for an elimination bracket.");
            }

            // Ensure every team has a unique seed
            var orderedTeams = teams
                .OrderBy(t => t.BracketSeed.HasValue ? 0 : 1)
                .ThenBy(t => t.BracketSeed ?? int.MaxValue)
                .ThenBy(t => t.TeamName)
                .ToList();

            var seededTeams = new Dictionary<int, Team>();
            int nextSeed = 1;

            foreach (var team in orderedTeams)
            {
                int seed = team.BracketSeed ?? nextSeed++;
                while (seededTeams.ContainsKey(seed))
                {
                    seed = nextSeed++;
                }

                seededTeams[seed] = team;
                if (!team.BracketSeed.HasValue)
                {
                    team.BracketSeed = seed;
                    _context.Teams.Update(team);
                }
            }
            await _context.SaveChangesAsync();

            int bracketSize = NextPowerOfTwo(seededTeams.Count);
            var seeding = GetSeedingOrder(bracketSize);

            var allMatches = new List<Match>();
            var currentRoundMatches = new List<Match>();
            var nextLinkPairs = new List<(Match child, Match parent)>();
            var loserLinkPairs = new List<(Match child, Match parent)>();
            List<Match>? semiFinalMatches = null;

            int roundNumber = 1;
            int matchNumber = 1;
            for (int i = 0; i < seeding.Length; i += 2)
            {
                seededTeams.TryGetValue(seeding[i], out var team1);
                seededTeams.TryGetValue(seeding[i + 1], out var team2);

                var match = new Match
                {
                    ScheduleId = scheduleId,
                    RoundName = $"Round of {bracketSize}",
                    RoundNumber = roundNumber,
                    MatchNumber = matchNumber,
                    Team1Id = team1?.TeamId,
                    Team2Id = team2?.TeamId,
                    MatchPosition = matchNumber % 2 == 1 ? 1 : 2
                };
                matchNumber++;

                if (team1 != null && team2 == null || team1 == null && team2 != null)
                {
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = team1?.TeamId ?? team2?.TeamId;
                }
                else if (team1 == null && team2 == null)
                {
                    match.IsBye = true;
                    match.Status = MatchStatus.Bye;
                    match.WinnerId = null;
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
            if (hasThirdPlace && semiFinalMatches != null && semiFinalMatches.Count == 2)
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
        case 2: return new[] { 1, 2 };
        case 4: return new[] { 1, 4, 2, 3 };
        case 8: return new[] { 1, 8, 4, 5, 3, 6, 2, 7 };
        case 16: return new[] { 1, 16, 8, 9, 5, 12, 4, 13, 6, 11, 3, 14, 7, 10, 2, 15 };
        case 32: return new[] { 1, 32, 17, 16, 9, 24, 25, 8, 5, 28, 21, 12, 13, 20, 29, 4, 3, 30, 19, 14, 11, 22, 27, 6, 7, 26, 23, 10, 15, 18, 31, 2 };
        case 64:
            var seeds = new int[64];
            for (int i = 0; i < 32; i++) { seeds[i * 2] = i + 1; seeds[i * 2 + 1] = 64 - i; }
            return seeds;
        default:
            int fallback = NextPowerOfTwo(size);
            var simple = Enumerable.Range(1, fallback).ToArray();
            return simple;
    }
        }

        // GET: /Match/GetMatchListing/5
        [HttpGet]
        public async Task<IActionResult> GetMatchListing(int id)
        {
            Console.WriteLine($"GetMatchListing called with id: {id}");

            var competition = await _context.Competitions
                .FirstOrDefaultAsync(c => c.ScheduleId == id);

            Console.WriteLine($"Competition found: Format = {competition!.Format}");

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

        // Server-side validation: scores must be integers between 0 and 21 (empty allowed)
        bool ValidateScoreString(string s, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(s)) return true;
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim());
            foreach (var p in parts)
            {
                if (!int.TryParse(p, out var v))
                {
                    reason = $"Invalid score value '{p}' (must be integer 0..21)";
                    return false;
                }
                if (v < 0 || v > 21)
                {
                    reason = $"Score {v} out of allowed range 0..21";
                    return false;
                }
            }
            return true;
        }

        string r1 = string.Empty, r2 = string.Empty;
        if (!ValidateScoreString(Team1Score, out r1) || !ValidateScoreString(Team2Score, out r2))
        {
            var msg = !string.IsNullOrEmpty(r1) ? r1 : r2;
            Console.WriteLine($"Score validation failed: {msg}");
            TempData["ErrorMessage"] = "invalid score max 21 accept";
            return RedirectToAction("CompDetails", "Competition", new { id = match.ScheduleId, tab = "match-listing" });
        }

        Console.WriteLine($"Match found - Team1Id: {match.Team1Id}, Team2Id: {match.Team2Id}");
        Console.WriteLine($"Current status: {match.Status}, Current winner: {match.WinnerId}");

        // *** DETECT IF THIS IS A SCORE EDIT ***
        bool isScoreEdit = match.Status == MatchStatus.Done && match.WinnerId.HasValue;
        int? oldWinnerId = match.WinnerId;

        // Update scores
        match.Team1Score = Team1Score;
        match.Team2Score = Team2Score;
        
        // Get current user ID
        var currentUserId = GetCurrentUserId();
        Console.WriteLine($"Current UserId: {currentUserId}");

        if (currentUserId.HasValue)
        {
            match.LastUpdatedByUserId = currentUserId.Value;
        }

        // Determine NEW winner
        Console.WriteLine("Calling DetermineWinnerId...");
        match.WinnerId = DetermineWinnerId(match.Team1Id, match.Team2Id, Team1Score, Team2Score);
        Console.WriteLine($"NEW WinnerId determined: {match.WinnerId}");

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

            // *** CHECK IF WINNER CHANGED ***
            if (isScoreEdit && oldWinnerId != match.WinnerId)
            {
                Console.WriteLine($"⚠ WINNER CHANGED! Old: {oldWinnerId}, New: {match.WinnerId}");
                Console.WriteLine("Recalculating bracket progression...");

                _context.Matches.Update(match);
                await _context.SaveChangesAsync();

                // Recalculate entire bracket progression
                await RecalculateBracketProgression(match);
            }
            else if (!isScoreEdit)
            {
                // First time completing this match
                Console.WriteLine("First time completion, calling AdvanceWinner...");
                await AdvanceWinner(match);
            }
            else
            {
                Console.WriteLine("Score edit but winner unchanged, updating match only");
            }
        }

        Console.WriteLine("Updating match in database...");
        _context.Matches.Update(match);
        await _context.SaveChangesAsync();

        // *** AUTO-UPDATE AWARD WINNERS IF FINAL MATCH COMPLETED ***
        if (match.Status == MatchStatus.Done && match.WinnerId.HasValue)
        {
            bool isFinalMatch = match.RoundName != null && 
                                match.RoundName.Contains("Final") && 
                                !match.RoundName.Contains("Semi");
            
            bool isThirdPlaceMatch = match.IsThirdPlaceMatch;

            if (isFinalMatch || isThirdPlaceMatch)
            {
                Console.WriteLine($"⭐ {(isFinalMatch ? "FINAL" : "3RD PLACE")} match completed! Updating award winners...");
                
                var hasAwards = await _context.Awards
                    .AnyAsync(a => a.ScheduleId == match.ScheduleId);

                if (hasAwards)
                {
                    await UpdateAwardWinnersForSchedule(match.ScheduleId);
                    Console.WriteLine("✓ Award winners updated successfully");
                }
            }
        }

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

        throw;
    }
}

// *** ADD THIS NEW HELPER METHOD ***
private async Task UpdateAwardWinnersForSchedule(int scheduleId)
{
    try
    {
        Console.WriteLine($"=== UpdateAwardWinnersForSchedule: {scheduleId} ===");

        var competition = await _context.Competitions
            .FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);

        if (competition == null)
        {
            Console.WriteLine("Competition not found");
            return;
        }

        // Get existing awards
        var awards = await _context.Awards
            .Where(a => a.ScheduleId == scheduleId)
            .ToListAsync();

        if (!awards.Any())
        {
            Console.WriteLine("No awards to update");
            return;
        }

        // Get winner info from matches
        var matches = await _context.Matches
            .Where(m => m.ScheduleId == scheduleId && !m.IsThirdPlaceMatch)
            .OrderByDescending(m => m.RoundNumber)
            .ToListAsync();

        var finalMatch = matches.FirstOrDefault(m => m.RoundName != null && 
                                                     m.RoundName.Contains("Final") && 
                                                     !m.RoundName.Contains("Semi"));
        
        var thirdPlaceMatch = await _context.Matches
            .FirstOrDefaultAsync(m => m.ScheduleId == scheduleId && m.IsThirdPlaceMatch);

        // Determine winners
        int? championTeamId = finalMatch?.WinnerId;
        int? firstRunnerUpTeamId = null;
        int? secondRunnerUpTeamId = thirdPlaceMatch?.WinnerId;

        if (championTeamId.HasValue && finalMatch != null)
        {
            firstRunnerUpTeamId = (finalMatch.Team1Id == championTeamId) 
                ? finalMatch.Team2Id 
                : finalMatch.Team1Id;
        }

        Console.WriteLine($"Champions: {championTeamId}, 1st Runner: {firstRunnerUpTeamId}, 2nd Runner: {secondRunnerUpTeamId}");

        // Update awards with winners
        bool updated = false;

        var championAward = awards.FirstOrDefault(a => a.Position == AwardPosition.Champion);
        if (championAward != null && championTeamId.HasValue && championAward.TeamId != championTeamId)
        {
            championAward.TeamId = championTeamId.Value;
            _context.Awards.Update(championAward);
            updated = true;
            Console.WriteLine($"✓ Updated Champion award to Team {championTeamId}");
        }

        var firstRunnerAward = awards.FirstOrDefault(a => a.Position == AwardPosition.FirstRunnerUp);
        if (firstRunnerAward != null && firstRunnerUpTeamId.HasValue && firstRunnerAward.TeamId != firstRunnerUpTeamId)
        {
            firstRunnerAward.TeamId = firstRunnerUpTeamId.Value;
            _context.Awards.Update(firstRunnerAward);
            updated = true;
            Console.WriteLine($"✓ Updated 1st Runner Up award to Team {firstRunnerUpTeamId}");
        }

        var secondRunnerAward = awards.FirstOrDefault(a => a.Position == AwardPosition.SecondRunnerUp);
        if (secondRunnerAward != null && secondRunnerUpTeamId.HasValue && secondRunnerAward.TeamId != secondRunnerUpTeamId)
        {
            secondRunnerAward.TeamId = secondRunnerUpTeamId.Value;
            _context.Awards.Update(secondRunnerAward);
            updated = true;
            Console.WriteLine($"✓ Updated 2nd Runner Up award to Team {secondRunnerUpTeamId}");
        }

        if (updated)
        {
            await _context.SaveChangesAsync();
            Console.WriteLine("✓ Award winners saved to database");
        }
        else
        {
            Console.WriteLine("No award updates needed");
        }

        Console.WriteLine("=== UpdateAwardWinnersForSchedule completed ===");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in UpdateAwardWinnersForSchedule: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
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

    var roundGroups = playoffMatches
        .Where(m => !m.IsThirdPlaceMatch)
        .GroupBy(m => m.RoundNumber)
        .OrderBy(g => g.Key)
        .ToList();

    var teamLookup = await _context.Teams
        .Where(t => t.ScheduleId == scheduleId)
        .ToDictionaryAsync(t => t.TeamId, t => t.TeamName);

    var (teamPairs, allResults) = BuildBracketMatrix(roundGroups, teamLookup);

    var roundHeaders = roundGroups
        .Select(g => g.First().RoundName ?? $"Round {g.Key}")
        .ToList();

    var bracketData = new
    {
        teams = teamPairs,
        results = allResults
    };

    object thirdPlace = null!;
    var thirdPlaceMatch = playoffMatches.FirstOrDefault(m => m.IsThirdPlaceMatch);
    if (thirdPlaceMatch != null)
    {
        thirdPlace = new
        {
            team1 = thirdPlaceMatch.Team1?.TeamName ?? "TBD",
            team2 = thirdPlaceMatch.Team2?.TeamName ?? "TBD",
            status = thirdPlaceMatch.WinnerId.HasValue ? "Done" : thirdPlaceMatch.Status.ToString(),
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
    var query = _context.Matches
        .Where(m => m.ScheduleId == scheduleId);

    if (competition.Format == CompetitionFormat.PoolPlay)
    {
        query = query.Where(m => m.RoundNumber >= 2);
    }

    var matches = await query
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

    var roundGroups = matches
        .Where(m => !m.IsThirdPlaceMatch)
        .GroupBy(m => m.RoundNumber)
        .OrderBy(g => g.Key)
        .ToList();

    var teamLookup = await _context.Teams
        .Where(t => t.ScheduleId == scheduleId)
        .ToDictionaryAsync(t => t.TeamId, t => t.TeamName);

    var (teamPairs, allResults) = BuildBracketMatrix(roundGroups, teamLookup);

    var roundHeaders = roundGroups
        .Select(g => g.First().RoundName ?? $"Round {g.Key}")
        .ToList();

    var bracketData = new
    {
        teams = teamPairs,
        results = allResults
    };

    object thirdPlace = null!;
    var thirdPlaceMatch = matches.FirstOrDefault(m => m.IsThirdPlaceMatch);
    if (thirdPlaceMatch != null)
    {
        thirdPlace = new
        {
            team1 = thirdPlaceMatch.Team1?.TeamName ?? "TBD",
            team2 = thirdPlaceMatch.Team2?.TeamName ?? "TBD",
            status = thirdPlaceMatch.WinnerId.HasValue ? "Done" : thirdPlaceMatch.Status.ToString(),
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

        // ADD this method after GenerateEmptyPlayoffBracket (around line 800+)

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

        var playoffMatches = await _context.Matches
            .Where(m => m.ScheduleId == scheduleId && m.RoundNumber >= 2)
            .ToListAsync();

        if (!playoffMatches.Any())
        {
            return Json(new { success = false, message = "Playoff bracket structure not found. Please restart competition." });
        }

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

                int points = (wins * 2) + losses;

                poolStandings.Add((team, wins, losses, points));
                Console.WriteLine($"  {team.TeamName}: W={wins}, L={losses}, Pts={points}");
            }

            var sorted = poolStandings
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.Wins)
                .ToList();

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

        // *** CROSS-POOL SEEDING ***
        var seededTeams = new List<Team>();
        var poolGroups = qualifiedTeams
            .GroupBy(qt => qt.PoolName)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(q => q.Position).ToList())
            .ToList();

        int numPools = poolGroups.Count;
        int winnersPerPool = competition.WinnersPerPool;

        // Pair pools (A vs B), for each pair produce cross match order:
        // A#1 vs B#2, B#1 vs A#2, then A#3,B#3, A#4,B#4...
        if (numPools >= 2)
        {
            for (int i = 0; i < numPools; i += 2)
            {
                // if odd last pool has no partner -> fall back to simple interleave
                if (i + 1 >= numPools)
                {
                    var lone = poolGroups[i];
                    for (int pos = 1; pos <= winnersPerPool; pos++)
                    {
                        var entryTuple = lone.FirstOrDefault(x => x.Position == pos);
                        if (entryTuple.Team != null) seededTeams.Add(entryTuple.Team);
                    }
                    break;
                }

                var A = poolGroups[i];
                var B = poolGroups[i + 1];

                // primary cross pairs for pos 1 and 2
                if (winnersPerPool >= 2)
                {
                    var a1Tuple = A.FirstOrDefault(x => x.Position == 1);
                    var b2Tuple = B.FirstOrDefault(x => x.Position == 2);
                    var A1 = a1Tuple.Team;
                    var B2 = b2Tuple.Team;
                    if (A1 != null && B2 != null) { seededTeams.Add(A1); seededTeams.Add(B2); }

                    var b1Tuple = B.FirstOrDefault(x => x.Position == 1);
                    var a2Tuple = A.FirstOrDefault(x => x.Position == 2);
                    var B1 = b1Tuple.Team;
                    var A2 = a2Tuple.Team;
                    if (B1 != null && A2 != null) { seededTeams.Add(B1); seededTeams.Add(A2); }
                }
                else
                {
                    // winnersPerPool == 1 -> pair A1 vs B1 (fallback)
                    var A1 = A.FirstOrDefault(x => x.Position == 1).Team;
                    var B1 = B.FirstOrDefault(x => x.Position == 1).Team;
                    if (A1 != null && B1 != null) { seededTeams.Add(A1); seededTeams.Add(B1); }
                }

                // for any remaining positions (3..winnersPerPool), append A#pos then B#pos
                for (int pos = 3; pos <= winnersPerPool; pos++)
                {
                    var aPos = A.FirstOrDefault(x => x.Position == pos).Team;
                    var bPos = B.FirstOrDefault(x => x.Position == pos).Team;
                    if (aPos != null) seededTeams.Add(aPos);
                    if (bPos != null) seededTeams.Add(bPos);
                }
            }
        }
        else
        {
            // fallback: interleave by position across pools (existing behavior)
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

        Console.WriteLine("Seeded playoff teams:");
        for (int i = 0; i < seededTeams.Count; i++)
        {
            Console.WriteLine($"  Seed {i + 1}: {seededTeams[i].TeamName}");
        }

        // *** ASSIGN TEAMS AND HANDLE BYES ***
        var firstRoundMatches = playoffMatches
            .Where(m => m.RoundNumber == 2 && !m.IsThirdPlaceMatch)
            .OrderBy(m => m.MatchNumber)
            .ToList();

        Console.WriteLine($"Assigning {seededTeams.Count} teams to {firstRoundMatches.Count} matches");

        // Assign teams to matches
        for (int i = 0; i < firstRoundMatches.Count; i++)
        {
            var match = firstRoundMatches[i];
            int team1Index = i * 2;
            int team2Index = i * 2 + 1;

            match.Team1Id = team1Index < seededTeams.Count ? seededTeams[team1Index].TeamId : null;
            match.Team2Id = team2Index < seededTeams.Count ? seededTeams[team2Index].TeamId : null;

            // *** Handle different BYE scenarios ***
            if (!match.Team1Id.HasValue && !match.Team2Id.HasValue)
            {
                // Both null (double BYE)
                match.Status = MatchStatus.Bye;
                match.IsBye = true;
                match.WinnerId = null;
                Console.WriteLine($"Match {match.MatchNumber}: DOUBLE BYE (no teams)");
            }
            else if (!match.Team1Id.HasValue || !match.Team2Id.HasValue)
            {
                // One null (single BYE)
                match.Status = MatchStatus.Bye;
                match.IsBye = true;
                match.WinnerId = match.Team1Id ?? match.Team2Id;
                Console.WriteLine($"Match {match.MatchNumber}: SINGLE BYE - Winner: {match.WinnerId}");
            }
            else
            {
                // Both teams present
                match.Status = MatchStatus.Active;
                match.IsBye = false;
                Console.WriteLine($"Match {match.MatchNumber}: {seededTeams[team1Index].TeamName} vs {seededTeams[team2Index].TeamName}");
            }

            _context.Matches.Update(match);
        }

        await _context.SaveChangesAsync();
        Console.WriteLine("Teams assigned to playoff matches");

        // *** AUTO-ADVANCE BYE MATCHES - FIXED VERSION ***
        var byeMatches = firstRoundMatches.Where(m => m.Status == MatchStatus.Bye).ToList();
        if (byeMatches.Any())
        {
            Console.WriteLine($"Auto-advancing {byeMatches.Count} bye matches");

            // *** FIX: Process ALL BYE matches, don't return early ***
            foreach (var byeMatch in byeMatches)
            {
                if (!byeMatch.Team1Id.HasValue && !byeMatch.Team2Id.HasValue)
                {
                    // Double BYE
                    Console.WriteLine($"Processing DOUBLE BYE match {byeMatch.MatchNumber}");
                    await AdvanceDoubleByeRecursive(byeMatch);
                }
                else
                {
                    // Single BYE
                    Console.WriteLine($"Processing SINGLE BYE match {byeMatch.MatchNumber}, winner: {byeMatch.WinnerId}");
                    await AdvanceWinner(byeMatch);
                }

                // *** IMPORTANT: Save all changes AFTER processing all BYEs ***
                await _context.SaveChangesAsync();
            }
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

private async Task AdvanceWinner(Match match)
{
    Console.WriteLine($"=== AdvanceWinner called for Match {match.MatchId} ===");

    if (!match.WinnerId.HasValue)
    {
        Console.WriteLine("No winner determined, skipping advancement");
        return;
    }

    if (match.NextMatchId.HasValue)
    {
        var nextMatch = await _context.Matches.FindAsync(match.NextMatchId.Value);
        if (nextMatch != null)
        {
            if (match.MatchPosition == 1)
            {
                nextMatch.Team1Id = match.WinnerId;
            }
            else
            {
                nextMatch.Team2Id = match.WinnerId;
            }

            _context.Matches.Update(nextMatch);
            await NormalizeMatchAfterFeed(nextMatch, match.MatchId);
        }
    }

   if (match.NextLoserMatchId.HasValue)
    {
        var thirdPlaceMatch = await _context.Matches.FindAsync(match.NextLoserMatchId.Value);
        if (thirdPlaceMatch != null)
        {
            int? loserId = null;
            if (match.Team1Id.HasValue && match.Team2Id.HasValue)
            {
                loserId = match.Team1Id == match.WinnerId ? match.Team2Id : match.Team1Id;
            }

            if (loserId.HasValue)
            {
                if (!thirdPlaceMatch.Team1Id.HasValue) thirdPlaceMatch.Team1Id = loserId;
                else if (!thirdPlaceMatch.Team2Id.HasValue) thirdPlaceMatch.Team2Id = loserId;
                else await ReplaceLoserIfFromSameSemi(match, thirdPlaceMatch, loserId);
            }

            await FinalizeThirdPlaceEntry(thirdPlaceMatch, match);
            _context.Matches.Update(thirdPlaceMatch);
        }
    }

    await _context.SaveChangesAsync();
    Console.WriteLine("=== AdvanceWinner completed ===");
}

private async Task FinalizeThirdPlaceEntry(Match thirdPlaceMatch, Match feederMatch)
{
    if (thirdPlaceMatch.Team1Id.HasValue && thirdPlaceMatch.Team2Id.HasValue)
    {
        thirdPlaceMatch.Status = MatchStatus.Active;
        thirdPlaceMatch.IsBye = false;
        return;
    }

    var otherSemiIsDead = await IsSiblingPathDead(thirdPlaceMatch.MatchId, feederMatch.MatchId);
     if (otherSemiIsDead)
    {
        var awardedTeamId = thirdPlaceMatch.Team1Id ?? thirdPlaceMatch.Team2Id;
        if (awardedTeamId.HasValue)
        {
            thirdPlaceMatch.Status = MatchStatus.Done;
            thirdPlaceMatch.IsBye = true;
            thirdPlaceMatch.WinnerId = awardedTeamId;

            if (!thirdPlaceMatch.Team1Id.HasValue && !thirdPlaceMatch.Team2Id.HasValue)
            {
                thirdPlaceMatch.Team1Id = awardedTeamId;
            }
        }
    }
}
private async Task<bool> IsSiblingPathDead(int thirdPlaceMatchId, int feederMatchId)
{
    var siblingLoserFeeds = await _context.Matches
        .Where(m => m.NextLoserMatchId == thirdPlaceMatchId && m.MatchId != feederMatchId)
        .ToListAsync();

    if (!siblingLoserFeeds.Any()) return true;

    foreach (var semi in siblingLoserFeeds)
    {
        if (semi.Status != MatchStatus.Bye) return false;
        if (!IsDeadBye(semi)) return false;
    }

    return true;
}

private async Task NormalizeMatchAfterFeed(Match nextMatch, int feederMatchId)
{
    Console.WriteLine($"NormalizeMatchAfterFeed for Match {nextMatch.MatchId}");

    var pairedMatchId = await GetSiblingFeederMatchId(nextMatch.MatchId, feederMatchId);
    bool pairedDead = pairedMatchId == null;

    if (!pairedDead)
    {
        var pairedMatch = await _context.Matches.FindAsync(pairedMatchId!.Value);
        pairedDead = pairedMatch == null ||
                     IsDeadBye(pairedMatch) ||
                     (!pairedMatch.Team1Id.HasValue && !pairedMatch.Team2Id.HasValue);
    }
    
    if (nextMatch.Team1Id.HasValue && nextMatch.Team2Id.HasValue)
    {
        nextMatch.Status = MatchStatus.Active;
        nextMatch.IsBye = false;
        nextMatch.WinnerId = null;
        _context.Matches.Update(nextMatch);
        await _context.SaveChangesAsync();
        return;
    }

    if (!pairedDead)
    {
        nextMatch.Status = MatchStatus.Pending;
        nextMatch.IsBye = false;
        nextMatch.WinnerId = null;
        _context.Matches.Update(nextMatch);
        await _context.SaveChangesAsync();
        return;
    }

    Console.WriteLine($"Match {nextMatch.MatchId} became orphan -> resolving as BYE");
    nextMatch.Status = MatchStatus.Bye;
    nextMatch.IsBye = true;
    nextMatch.WinnerId = nextMatch.Team1Id ?? nextMatch.Team2Id;

    _context.Matches.Update(nextMatch);
    await _context.SaveChangesAsync();

    if (nextMatch.WinnerId.HasValue)
    {
        await AdvanceWinner(nextMatch);
    }
    else
    {
        await AdvanceDoubleByeRecursive(nextMatch);
    }
}

private async Task<int?> GetSiblingFeederMatchId(int parentMatchId, int feederMatchId)
{
    return await _context.Matches
        .Where(m => m.NextMatchId == parentMatchId && m.MatchId != feederMatchId)
        .Select(m => (int?)m.MatchId)
        .FirstOrDefaultAsync();
}


private static bool IsDeadBye(Match match)
{
    if (!match.IsBye) return false;

    bool noTeams = !match.Team1Id.HasValue && !match.Team2Id.HasValue;
    bool singleTeam = match.Team1Id.HasValue ^ match.Team2Id.HasValue;

    return (match.Status == MatchStatus.Bye || match.WinnerId == null) &&
           (noTeams || singleTeam);
}


private async Task ReplaceLoserIfFromSameSemi(Match semi, Match thirdPlace, int? loserId)
{
    if (!loserId.HasValue) return;

    if (thirdPlace.Team1Id == semi.Team1Id || thirdPlace.Team1Id == semi.Team2Id)
    {
        thirdPlace.Team1Id = loserId;
    }
    else if (thirdPlace.Team2Id == semi.Team1Id || thirdPlace.Team2Id == semi.Team2Id)
    {
        thirdPlace.Team2Id = loserId;
    }
    else
    {
        return;
    }

    if (thirdPlace.Team1Id.HasValue && thirdPlace.Team2Id.HasValue)
    {
        thirdPlace.Status = MatchStatus.Active;
        thirdPlace.IsBye = false;
    }

    _context.Matches.Update(thirdPlace);
    await _context.SaveChangesAsync();
}
private async Task RecalculateBracketProgression(Match editedMatch)
{
    Console.WriteLine($"=== RecalculateBracketProgression for Match {editedMatch.MatchId} ===");
    
    // Get all downstream matches (matches that depend on this one)
    var affectedMatches = new List<Match>();
    await CollectAffectedMatches(editedMatch, affectedMatches);
    
    Console.WriteLine($"Found {affectedMatches.Count} affected matches");
    
    // *** STEP 1: CLEAR TEAMS AND RESET ALL AFFECTED MATCHES ***
    foreach (var match in affectedMatches.OrderByDescending(m => m.RoundNumber))
    {
        Console.WriteLine($"Resetting Match {match.MatchId} ({match.RoundName})");
        
        // Clear teams
        match.Team1Id = null;
        match.Team2Id = null;
        
        // Reset match state
        match.WinnerId = null;
        match.Team1Score = null;
        match.Team2Score = null;
        match.Status = MatchStatus.Pending;
        match.IsBye = false; // ⬅️ ADD THIS: Reset BYE flag
        
        Console.WriteLine($"  ✓ Cleared both teams and reset to Pending");
        
        _context.Matches.Update(match);
    }
    
    await _context.SaveChangesAsync();
    Console.WriteLine("✓ All affected matches cleared and reset");
    
    // *** STEP 2: RE-ADVANCE EDITED MATCH WITH NEW WINNER ***
    Console.WriteLine($"Re-advancing edited match {editedMatch.MatchId} with new winner: {editedMatch.WinnerId}");
    await AdvanceWinner(editedMatch);
    await _context.SaveChangesAsync();
    Console.WriteLine("✓ Edited match re-advanced");
    
    // *** STEP 3: FIND AND RE-ADVANCE ALL MATCHES IN THE SAME ROUND ***
    var sameRoundMatches = await _context.Matches
        .Where(m => m.ScheduleId == editedMatch.ScheduleId &&
                   m.RoundNumber == editedMatch.RoundNumber &&
                   !m.IsThirdPlaceMatch &&
                   m.MatchId != editedMatch.MatchId) // Exclude the edited match
        .ToListAsync();
    
    Console.WriteLine($"Found {sameRoundMatches.Count} other matches in the same round");
    
    // *** STEP 4: RE-ADVANCE ALL COMPLETED MATCHES IN THE SAME ROUND ***
    foreach (var sameRoundMatch in sameRoundMatches)
    {
        // Check if match has scores (completed match)
        if (!string.IsNullOrEmpty(sameRoundMatch.Team1Score) && 
            !string.IsNullOrEmpty(sameRoundMatch.Team2Score) &&
            sameRoundMatch.WinnerId.HasValue)
        {
            Console.WriteLine($"Re-advancing completed match {sameRoundMatch.MatchId}, winner: {sameRoundMatch.WinnerId}");
            await AdvanceWinner(sameRoundMatch);
            await _context.SaveChangesAsync();
        }
        // ⬇️ NEW: Check if match is a BYE
        else if (sameRoundMatch.IsBye || sameRoundMatch.Status == MatchStatus.Bye)
        {
            Console.WriteLine($"Re-advancing BYE match {sameRoundMatch.MatchId}");
            
            // Re-determine BYE status based on current teams
            if (!sameRoundMatch.Team1Id.HasValue && !sameRoundMatch.Team2Id.HasValue)
            {
                // Double BYE
                sameRoundMatch.Status = MatchStatus.Bye;
                sameRoundMatch.IsBye = true;
                sameRoundMatch.WinnerId = null;
                _context.Matches.Update(sameRoundMatch);
                await _context.SaveChangesAsync();
                
                await AdvanceDoubleByeRecursive(sameRoundMatch);
            }
            else if (!sameRoundMatch.Team1Id.HasValue || !sameRoundMatch.Team2Id.HasValue)
            {
                // Single BYE
                sameRoundMatch.Status = MatchStatus.Bye;
                sameRoundMatch.IsBye = true;
                sameRoundMatch.WinnerId = sameRoundMatch.Team1Id ?? sameRoundMatch.Team2Id;
                _context.Matches.Update(sameRoundMatch);
                await _context.SaveChangesAsync();
                
                Console.WriteLine($"  Single BYE - Winner: {sameRoundMatch.WinnerId}");
                await AdvanceWinner(sameRoundMatch);
            }
            
            await _context.SaveChangesAsync();
        }
    }
    
    Console.WriteLine("✓ All same-round matches re-advanced");
    
    // *** STEP 5: CHECK AND RE-ADVANCE BYE MATCHES IN AFFECTED ROUNDS ***
    var affectedRounds = affectedMatches
        .Select(m => m.RoundNumber)
        .Distinct()
        .OrderBy(r => r)
        .ToList();
    
    foreach (var roundNum in affectedRounds)
    {
        var byeMatchesInRound = await _context.Matches
            .Where(m => m.ScheduleId == editedMatch.ScheduleId &&
                       m.RoundNumber == roundNum &&
                       (m.IsBye || m.Status == MatchStatus.Bye))
            .ToListAsync();
        
        if (byeMatchesInRound.Any())
        {
            Console.WriteLine($"Re-checking BYE matches in round {roundNum}: {byeMatchesInRound.Count} found");
            
            foreach (var byeMatch in byeMatchesInRound)
            {
                // Refresh match from DB to get latest state
                var freshMatch = await _context.Matches.FindAsync(byeMatch.MatchId);
                if (freshMatch == null) continue;
                
                if (!freshMatch.Team1Id.HasValue && !freshMatch.Team2Id.HasValue)
                {
                    Console.WriteLine($"  Match {freshMatch.MatchId}: Double BYE");
                    await AdvanceDoubleByeRecursive(freshMatch);
                }
                else if ((!freshMatch.Team1Id.HasValue || !freshMatch.Team2Id.HasValue) && 
                         freshMatch.WinnerId.HasValue)
                {
                    Console.WriteLine($"  Match {freshMatch.MatchId}: Single BYE, re-advancing winner {freshMatch.WinnerId}");
                    await AdvanceWinner(freshMatch);
                }
                
                await _context.SaveChangesAsync();
            }
        }
    }
    
    // *** STEP 6: RE-PROCESS ANY OTHER COMPLETED MATCHES IN DOWNSTREAM ROUNDS ***
    var downstreamCompletedMatches = await _context.Matches
        .Where(m => affectedMatches.Select(am => am.MatchId).Contains(m.MatchId) &&
                   !string.IsNullOrEmpty(m.Team1Score) && 
                   !string.IsNullOrEmpty(m.Team2Score) &&
                   m.Status != MatchStatus.Bye)
        .OrderBy(m => m.RoundNumber)
        .ThenBy(m => m.MatchNumber)
        .ToListAsync();
    
    Console.WriteLine($"Re-processing {downstreamCompletedMatches.Count} downstream completed matches");
    
    foreach (var match in downstreamCompletedMatches)
    {
        Console.WriteLine($"Re-processing Match {match.MatchId} ({match.RoundName})");
        
        // Refresh match from DB to get latest teams
        var updatedMatch = await _context.Matches.FindAsync(match.MatchId);
        
        if (updatedMatch != null && 
            updatedMatch.Team1Id.HasValue && 
            updatedMatch.Team2Id.HasValue)
        {
            // Both teams assigned, recalculate winner
            updatedMatch.WinnerId = DetermineWinnerId(
                updatedMatch.Team1Id, 
                updatedMatch.Team2Id, 
                updatedMatch.Team1Score, 
                updatedMatch.Team2Score
            );
            
            if (updatedMatch.WinnerId.HasValue)
            {
                updatedMatch.Status = MatchStatus.Done;
                _context.Matches.Update(updatedMatch);
                await _context.SaveChangesAsync();
                
                // Re-advance this match's winner
                await AdvanceWinner(updatedMatch);
                await _context.SaveChangesAsync();
                
                Console.WriteLine($"✓ Re-processed Match {updatedMatch.MatchId}, winner: {updatedMatch.WinnerId}");
            }
        }
        else
        {
            Console.WriteLine($"⚠ Match {match.MatchId} cannot be re-processed (missing teams)");
        }
    }
    
    // *** STEP 7: UPDATE AWARDS IF FINAL WAS AFFECTED ***
    var finalMatch = affectedMatches.FirstOrDefault(m => 
        m.RoundName != null && 
        m.RoundName.Contains("Final") && 
        !m.RoundName.Contains("Semi")
    );
    
    if (finalMatch != null)
    {
        Console.WriteLine("Final match was affected, updating awards...");
        await UpdateAwardWinnersForSchedule(editedMatch.ScheduleId);
    }
    
    Console.WriteLine("=== RecalculateBracketProgression completed ===");
}

/// <summary>
/// Recursively collects all matches affected by editing a match score
/// </summary>
private async Task CollectAffectedMatches(Match match, List<Match> affected)
{
    Console.WriteLine($"Collecting affected matches from Match {match.MatchId}");
    
    // Add winner's next match (e.g., Semi-Final → Final)
    if (match.NextMatchId.HasValue)
    {
        var nextMatch = await _context.Matches.FindAsync(match.NextMatchId.Value);
        if (nextMatch != null && !affected.Any(m => m.MatchId == nextMatch.MatchId))
        {
            Console.WriteLine($"  Adding NextMatch {nextMatch.MatchId} ({nextMatch.RoundName})");
            affected.Add(nextMatch);
            await CollectAffectedMatches(nextMatch, affected);
        }
    }
    
    // Add loser's next match (e.g., Semi-Final → 3rd Place Match)
    if (match.NextLoserMatchId.HasValue)
    {
        var loserMatch = await _context.Matches.FindAsync(match.NextLoserMatchId.Value);
        if (loserMatch != null && !affected.Any(m => m.MatchId == loserMatch.MatchId))
        {
            Console.WriteLine($"  Adding NextLoserMatch {loserMatch.MatchId} ({loserMatch.RoundName})");
            affected.Add(loserMatch);
            await CollectAffectedMatches(loserMatch, affected);
        }
    }
}

        /// <summary>
        /// Recursively propagates double BYEs through the bracket until reaching a non-BYE match
        /// </summary>
        private async Task AdvanceDoubleByeRecursive(Match doubleByeMatch)
        {
            Console.WriteLine($"=== AdvanceDoubleByeRecursive for Match {doubleByeMatch.MatchId} ===");

            if (!doubleByeMatch.NextMatchId.HasValue)
            {
                Console.WriteLine("No next match, stopping recursion");
                return;
            }

            var nextMatch = await _context.Matches.FindAsync(doubleByeMatch.NextMatchId.Value);
            if (nextMatch == null)
            {
                Console.WriteLine("ERROR: Next match not found");
                return;
            }

            Console.WriteLine($"Propagating double BYE to NextMatch {nextMatch.MatchId}");

            // Set the appropriate position to null
            if (doubleByeMatch.MatchPosition == 1)
            {
                nextMatch.Team1Id = null;
            }
            else if (doubleByeMatch.MatchPosition == 2)
            {
                nextMatch.Team2Id = null;
            }

            // Determine new status of next match
            if (!nextMatch.Team1Id.HasValue && !nextMatch.Team2Id.HasValue)
            {
                // Next match is also a double BYE
                nextMatch.Status = MatchStatus.Bye;
                nextMatch.IsBye = true;
                nextMatch.WinnerId = null;
                Console.WriteLine($"NextMatch {nextMatch.MatchId} is now a DOUBLE BYE");

                _context.Matches.Update(nextMatch);

                // Recursively propagate
                await AdvanceDoubleByeRecursive(nextMatch);
            }
            else if (!nextMatch.Team1Id.HasValue || !nextMatch.Team2Id.HasValue)
            {
                // Next match is a single BYE
                nextMatch.Status = MatchStatus.Bye;
                nextMatch.IsBye = true;
                nextMatch.WinnerId = nextMatch.Team1Id ?? nextMatch.Team2Id;
                Console.WriteLine($"NextMatch {nextMatch.MatchId} is now a SINGLE BYE, winner: {nextMatch.WinnerId}");

                _context.Matches.Update(nextMatch);

                // Advance this single BYE
                await AdvanceWinner(nextMatch);
            }
            else
            {
                // Next match has both teams - it can proceed normally
                nextMatch.Status = MatchStatus.Active;
                nextMatch.IsBye = false;
                Console.WriteLine($"NextMatch {nextMatch.MatchId} can proceed (both teams present)");

                _context.Matches.Update(nextMatch);
            }
        }

        /// <summary>
        /// Improved winner determination with detailed set-by-set analysis
        /// </summary>
        private int? DetermineWinnerId(int? team1Id, int? team2Id, string? team1Score, string? team2Score)
        {
            try
            {
                Console.WriteLine($"=== DetermineWinnerId ===");
                Console.WriteLine($"Team1Id: {team1Id}, Team2Id: {team2Id}");
                Console.WriteLine($"Team1Score: '{team1Score}', Team2Score: '{team2Score}'");

                // Validate inputs
                if (!team1Id.HasValue || !team2Id.HasValue)
                {
                    Console.WriteLine("ERROR: One or both team IDs are null");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(team1Score) || string.IsNullOrWhiteSpace(team2Score))
                {
                    Console.WriteLine("Scores are null/empty, returning null");
                    return null;
                }

                // Parse set scores
                var setScores1 = team1Score.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => int.TryParse(s, out var val) ? val : 0)
                    .ToList();

                var setScores2 = team2Score.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => int.TryParse(s, out var val) ? val : 0)
                    .ToList();

                Console.WriteLine($"Parsed setScores1: [{string.Join(", ", setScores1)}]");
                Console.WriteLine($"Parsed setScores2: [{string.Join(", ", setScores2)}]");

                // Validate equal number of sets
                if (setScores1.Count == 0 || setScores2.Count == 0)
                {
                    Console.WriteLine("ERROR: No valid scores found");
                    return null;
                }

                if (setScores1.Count != setScores2.Count)
                {
                    Console.WriteLine($"ERROR: Mismatched set counts ({setScores1.Count} vs {setScores2.Count})");
                    return null;
                }

                // Count sets won by each team
                int team1SetWins = 0;
                int team2SetWins = 0;

                for (int i = 0; i < setScores1.Count; i++)
                {
                    int score1 = setScores1[i];
                    int score2 = setScores2[i];

                    if (score1 > score2)
                    {
                        team1SetWins++;
                        Console.WriteLine($"  Set {i + 1}: Team1 wins ({score1} - {score2})");
                    }
                    else if (score2 > score1)
                    {
                        team2SetWins++;
                        Console.WriteLine($"  Set {i + 1}: Team2 wins ({score1} - {score2})");
                    }
                    else
                    {
                        Console.WriteLine($"  Set {i + 1}: Draw/Tie ({score1} - {score2})");
                    }
                }

                Console.WriteLine($"Final: Team1 won {team1SetWins} sets, Team2 won {team2SetWins} sets");

                // Determine overall winner (best of sets)
                if (team1SetWins > team2SetWins)
                {
                    Console.WriteLine($"✓ Team1 WINS overall (Team1Id: {team1Id})");
                    return team1Id;
                }
                else if (team2SetWins > team1SetWins)
                {
                    Console.WriteLine($"✓ Team2 WINS overall (Team2Id: {team2Id})");
                    return team2Id;
                }
                else
                {
                    Console.WriteLine("⚠ Match is TIED (equal sets won) - returning null");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in DetermineWinnerId: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        private (List<string[]>, List<List<int[]>>) BuildBracketMatrix(
    List<IGrouping<int, Match>> roundGroups,
    Dictionary<int, string> teamLookup)
{
    if (roundGroups == null || roundGroups.Count == 0)
        return (new List<string[]>(), new List<List<int[]>>());

    var firstRoundMatches = roundGroups[0]
        .Where(m => m.Team1Id.HasValue || m.Team2Id.HasValue)
        .OrderBy(m => m.MatchNumber)
        .ToList();

    int actualSlots = firstRoundMatches.Count;
    int bracketSlots = NextPowerOfTwo(Math.Max(1, actualSlots));

    var teamPairs = new List<string[]>();
    for (int i = 0; i < bracketSlots; i++)
    {
        if (i < actualSlots)
        {
            teamPairs.Add(BuildTeamPair(firstRoundMatches[i], teamLookup));
        }
        else
        {
            teamPairs.Add(new[] { "BYE", "BYE" });
        }
    }

    var results = new List<List<int[]>>();
    int expectedMatches = bracketSlots;

    for (int roundIndex = 0; roundIndex < roundGroups.Count; roundIndex++)
    {
        var roundMatches = roundGroups[roundIndex]
            .Where(m => m.Team1Id.HasValue || m.Team2Id.HasValue)
            .OrderBy(m => m.MatchNumber)
            .ToList();

        var roundScores = new List<int[]>();
        for (int i = 0; i < expectedMatches; i++)
        {
            if (i < roundMatches.Count)
            {
                roundScores.Add(BuildScoreTuple(roundMatches[i]));
            }
            else
            {
                // fill with BYE result so downstream rounds auto-advance
                roundScores.Add(new[] { 1, 0 });
            }
        }

        results.Add(roundScores);
        expectedMatches = Math.Max(1, expectedMatches / 2);
    }

    return (teamPairs, results);
}

        private string[] BuildTeamPair(Match? match, Dictionary<int, string> teamLookup)
        {
            string Resolve(int? teamId) =>
                teamId.HasValue && teamLookup.TryGetValue(teamId.Value, out var name) ? name : "TBD";

            if (match == null)
            {
                return new[] { "TBD", "TBD" };
            }

            var team1Name = match.Team1?.TeamName ?? Resolve(match.Team1Id);
            var team2Name = match.Team2?.TeamName ?? Resolve(match.Team2Id);

            if (match.IsBye && match.WinnerId.HasValue)
            {
                // ensure single-bye matches show the advancing team
                if (!match.Team1Id.HasValue) team1Name = "BYE";
                if (!match.Team2Id.HasValue) team2Name = Resolve(match.WinnerId);
            }

            return new[] { team1Name ?? "TBD", team2Name ?? "TBD" };
        }

        private int[] BuildScoreTuple(Match? match)
        {
            if (match == null)
            {
                return Array.Empty<int>(); // ⬅️ CHANGED: Return empty array instead of [0,0]
            }

            bool completed = match.Status == MatchStatus.Done ||
                     (match.Status == MatchStatus.Bye && match.WinnerId.HasValue);

            if (!completed)
            {
                return Array.Empty<int>(); // ⬅️ CHANGED: Return empty array for pending matches
            }

            // ⬇️ NEW: Handle BYE matches explicitly
            if (match.IsBye && match.WinnerId.HasValue)
            {
                return match.WinnerId == match.Team1Id ? new[] { 1, 0 } : new[] { 0, 1 };
            }

            if (!string.IsNullOrEmpty(match.Team1Score) && !string.IsNullOrEmpty(match.Team2Score))
            {
                int team1Wins = 0, team2Wins = 0;
                var team1Sets = match.Team1Score.Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();
                var team2Sets = match.Team2Score.Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();

                for (int i = 0; i < Math.Min(team1Sets.Length, team2Sets.Length); i++)
                {
                    if (team1Sets[i] > team2Sets[i]) team1Wins++;
                    else if (team2Sets[i] > team1Sets[i]) team2Wins++;
                }

                return new[] { team1Wins, team2Wins };
            }

            // ⬇️ FALLBACK: If match is marked Done but has no scores, use WinnerId
            if (match.WinnerId.HasValue)
            {
                return match.WinnerId == match.Team1Id ? new[] { 1, 0 } : new[] { 0, 1 };
            }

            return Array.Empty<int>(); // ⬅️ CHANGED: Return empty array as final fallback
        }

        private int NextPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value) power <<= 1;
            return power;
        }

        [HttpGet]
public async Task<IActionResult> DebugPlayoffMatches(int scheduleId)
{
    var playoffMatches = await _context.Matches
        .Where(m => m.ScheduleId == scheduleId && m.RoundNumber >= 2)
        .Include(m => m.Team1)
        .Include(m => m.Team2)
        .OrderBy(m => m.RoundNumber)
        .ThenBy(m => m.MatchNumber)
        .Select(m => new
        {
            m.MatchId,
            m.MatchNumber,
            m.RoundName,
            m.RoundNumber,
            m.Status,
            m.IsBye,
            Team1Id = m.Team1Id,
            Team1Name = m.Team1 != null ? m.Team1.TeamName : "NULL",
            Team2Id = m.Team2Id,
            Team2Name = m.Team2 != null ? m.Team2.TeamName : "NULL",
            m.WinnerId,
            m.NextMatchId,
            m.MatchPosition
        })
        .ToListAsync();

    return Json(playoffMatches);
}
    }
}