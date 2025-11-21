using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PicklePlay.Controllers
{
    public class AwardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AwardController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int? GetCurrentUserId()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (int.TryParse(userIdString, out int userId))
            {
                return userId;
            }
            return null;
        }

        [HttpGet]
        public async Task<IActionResult> GetAwards(int scheduleId)
        {
 
            try
            {
                var currentUserId = GetCurrentUserId();

                var schedule = await _context.Schedules
                    .Include(s => s.Participants)
                        .ThenInclude(p => p.User)
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null) return PartialView("~/Views/Competition/_EmptyResult.cshtml", "Competition not found.");

                var competition = await _context.Competitions.FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);
                if (competition == null) return PartialView("~/Views/Competition/_EmptyResult.cshtml", "Competition details not found.");

                var organizersAndStaff = schedule.Participants
                    .Where(p => p.Role == ParticipantRole.Organizer || p.Role == ParticipantRole.Staff)
                    .ToList();

                bool isOrganizer = organizersAndStaff.Any(o => o.UserId == currentUserId);

                var awards = await _context.Awards
                    .Include(a => a.Team)
                        .ThenInclude(t => t!.TeamMembers)
                        .ThenInclude(tm => tm.User)
                    .Where(a => a.ScheduleId == scheduleId)
                    .ToListAsync();

                var viewModel = new AwardViewModel
                {
                    ScheduleId = scheduleId,
                    CompetitionTitle = schedule.GameName ?? "Competition",
                    Format = competition.Format,
                    Status = schedule.Status ?? ScheduleStatus.PendingSetup,
                    IsOrganizer = isOrganizer,
                    HasAwards = awards.Any(),
                    HasThirdPlace = competition.ThirdPlaceMatch
                };

                if (awards.Any())
                {
                    // Map Champion
                    var champion = awards.FirstOrDefault(a => a.Position == AwardPosition.Champion);
                    if (champion != null)
                    {
                        if (champion.TeamId.HasValue && champion.TeamId.Value > 0 && champion.Team != null)
                        {
                            viewModel.Champion = MapWinnerInfo(champion);
                        }
                        viewModel.AwardName = champion.AwardName;
                        viewModel.AwardType = champion.AwardType;
                        viewModel.Description = champion.Description;
                    }

                    // Map 1st Runner
                    var firstRunner = awards.FirstOrDefault(a => a.Position == AwardPosition.FirstRunnerUp);
                    if (firstRunner != null && firstRunner.TeamId.HasValue && firstRunner.TeamId.Value > 0 && firstRunner.Team != null)
                    {
                        viewModel.FirstRunnerUp = MapWinnerInfo(firstRunner);
                    }

                    // Map 2nd Runner
                    var secondRunner = awards.FirstOrDefault(a => a.Position == AwardPosition.SecondRunnerUp);
                    if (secondRunner != null && secondRunner.TeamId.HasValue && secondRunner.TeamId.Value > 0 && secondRunner.Team != null)
                    {
                        viewModel.SecondRunnerUp = MapWinnerInfo(secondRunner);
                    }
                }

                return PartialView("~/Views/Competition/_ResultsView.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                return PartialView("~/Views/Competition/_EmptyResult.cshtml", $"Error loading results: {ex.Message}");
            }
        }

        private AwardWinnerInfo MapWinnerInfo(Award award)
        {
            return new AwardWinnerInfo
            {
                TeamId = award.TeamId!.Value,
                TeamName = award.Team!.TeamName ?? "Unknown Team",
                PlayerNames = award.Team.TeamMembers?
                                .Select(tm => tm.User?.Username ?? "Unknown")
                                .ToList() ?? new List<string>(),
                AwardType = award.AwardType,
                AwardName = award.AwardName,
                Description = award.Description,
                AwardedDate = award.AwardedDate,
                Position = award.Position
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAwards(int scheduleId, string awardName, AwardType awardType, string? description)
        {
            try
            {
                var competition = await _context.Competitions.FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);
                if (competition == null) return Json(new { success = false, message = "Competition not found." });

                // 1. Check if awards exist -> Update them
                var existingAwards = await _context.Awards.Where(a => a.ScheduleId == scheduleId).ToListAsync();
                if (existingAwards.Any())
                {
                    foreach (var award in existingAwards)
                    {
                        award.AwardName = awardName;
                        award.AwardType = awardType;
                        award.Description = description;
                    }
                    _context.Awards.UpdateRange(existingAwards);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Award settings updated successfully!" });
                }

                // 2. Determine Winners based on Format
                int? championId = null;
                int? firstRunnerId = null;
                int? secondRunnerId = null;

                if (competition.Format == CompetitionFormat.RoundRobin)
                {
                    // --- ROUND ROBIN LOGIC ---
                    var rrWinners = await CalculateRoundRobinWinners(scheduleId);
                    championId = rrWinners.ChampionId;
                    firstRunnerId = rrWinners.FirstRunnerId;
                    secondRunnerId = rrWinners.SecondRunnerId;
                }
                else
                {
                    // --- POOL PLAY / ELIMINATION LOGIC (Final Match) ---
                    var matches = await _context.Matches
                        .Where(m => m.ScheduleId == scheduleId && !m.IsThirdPlaceMatch)
                        .OrderByDescending(m => m.RoundNumber)
                        .ToListAsync();

                    var finalMatch = matches.FirstOrDefault(m => m.RoundName != null &&
                                                                 m.RoundName.Contains("Final") &&
                                                                 !m.RoundName.Contains("Semi"));

                    var thirdPlaceMatch = await _context.Matches
                        .FirstOrDefaultAsync(m => m.ScheduleId == scheduleId && m.IsThirdPlaceMatch);

                    championId = finalMatch?.WinnerId;
                    if (championId.HasValue && finalMatch != null)
                    {
                        firstRunnerId = (finalMatch.Team1Id == championId) ? finalMatch.Team2Id : finalMatch.Team1Id;
                    }
                    secondRunnerId = thirdPlaceMatch?.WinnerId;
                }

                // 3. Create Awards
                var newAwards = new List<Award>
                {
                    new Award { ScheduleId = scheduleId, AwardName = awardName, AwardType = awardType, Description = description, Position = AwardPosition.Champion, TeamId = championId, AwardedDate = DateTime.UtcNow, SetByUserId = 1 },
                    new Award { ScheduleId = scheduleId, AwardName = awardName, AwardType = awardType, Description = description, Position = AwardPosition.FirstRunnerUp, TeamId = firstRunnerId, AwardedDate = DateTime.UtcNow, SetByUserId = 1 }
                };

                // Round Robin always has 3rd place logic if there are enough teams, 
                // Elimination depends on ThirdPlaceMatch flag
                if (competition.Format == CompetitionFormat.RoundRobin || competition.ThirdPlaceMatch)
                {
                    newAwards.Add(new Award
                    {
                        ScheduleId = scheduleId,
                        AwardName = awardName,
                        AwardType = awardType,
                        Description = description,
                        Position = AwardPosition.SecondRunnerUp,
                        TeamId = secondRunnerId,
                        AwardedDate = DateTime.UtcNow,
                        SetByUserId = 1
                    });
                }

                await _context.Awards.AddRangeAsync(newAwards);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Awards created successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAwardWinners(int scheduleId)
        {
            try
            {
                var competition = await _context.Competitions.FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);
                if (competition == null) return Json(new { success = false, message = "Competition not found." });

                var awards = await _context.Awards.Where(a => a.ScheduleId == scheduleId).ToListAsync();
                if (!awards.Any()) return Json(new { success = false, message = "No awards configured." });

                int? championId = null;
                int? firstRunnerId = null;
                int? secondRunnerId = null;

                if (competition.Format == CompetitionFormat.RoundRobin)
                {
                    // --- ROUND ROBIN LOGIC ---
                    var rrWinners = await CalculateRoundRobinWinners(scheduleId);
                    championId = rrWinners.ChampionId;
                    firstRunnerId = rrWinners.FirstRunnerId;
                    secondRunnerId = rrWinners.SecondRunnerId;
                }
                else
                {
                    // --- ELIMINATION / POOL PLAY LOGIC ---
                    var matches = await _context.Matches
                        .Where(m => m.ScheduleId == scheduleId && !m.IsThirdPlaceMatch)
                        .OrderByDescending(m => m.RoundNumber)
                        .ToListAsync();

                    var finalMatch = matches.FirstOrDefault(m => m.RoundName != null &&
                                                                 m.RoundName.Contains("Final") &&
                                                                 !m.RoundName.Contains("Semi"));

                    var thirdPlaceMatch = await _context.Matches
                        .FirstOrDefaultAsync(m => m.ScheduleId == scheduleId && m.IsThirdPlaceMatch);

                    championId = finalMatch?.WinnerId;
                    if (championId.HasValue && finalMatch != null)
                    {
                        firstRunnerId = (finalMatch.Team1Id == championId) ? finalMatch.Team2Id : finalMatch.Team1Id;
                    }
                    secondRunnerId = thirdPlaceMatch?.WinnerId;
                }

                // Update
                var championAward = awards.FirstOrDefault(a => a.Position == AwardPosition.Champion);
                if (championAward != null && championId.HasValue) championAward.TeamId = championId.Value;

                var firstRunnerAward = awards.FirstOrDefault(a => a.Position == AwardPosition.FirstRunnerUp);
                if (firstRunnerAward != null && firstRunnerId.HasValue) firstRunnerAward.TeamId = firstRunnerId.Value;

                var secondRunnerAward = awards.FirstOrDefault(a => a.Position == AwardPosition.SecondRunnerUp);
                if (secondRunnerAward != null && secondRunnerId.HasValue) secondRunnerAward.TeamId = secondRunnerId.Value;

                _context.Awards.UpdateRange(awards);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Award winners updated!",
                    championTeamId = championId,
                    firstRunnerUpTeamId = firstRunnerId,
                    secondRunnerUpTeamId = secondRunnerId
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // --- HELPER: Calculate RR Winners using Sort Logic ---
        private async Task<(int? ChampionId, int? FirstRunnerId, int? SecondRunnerId)> CalculateRoundRobinWinners(int scheduleId)
{
    var teams = await _context.Teams
        .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
        .ToListAsync();

    var matches = await _context.Matches
        .Where(m => m.ScheduleId == scheduleId && m.Status == MatchStatus.Done)
        .ToListAsync();

    var statsList = new List<RRStats>();

    foreach (var team in teams)
    {
        var st = new RRStats { TeamId = team.TeamId };
        var teamMatches = matches.Where(m => m.Team1Id == team.TeamId || m.Team2Id == team.TeamId).ToList();

        foreach (var match in teamMatches)
        {
            bool isTeam1 = match.Team1Id == team.TeamId;
            var teamScoreStr = isTeam1 ? match.Team1Score : match.Team2Score;
            var oppScoreStr = isTeam1 ? match.Team2Score : match.Team1Score;

            if (string.IsNullOrEmpty(teamScoreStr) || string.IsNullOrEmpty(oppScoreStr)) continue;

            var teamSets = teamScoreStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();
            var oppSets = oppScoreStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();

            int setWins = 0;
            int teamTotal = 0, oppTotal = 0;

            for (int i = 0; i < Math.Min(teamSets.Length, oppSets.Length); i++)
            {
                if (teamSets[i] > oppSets[i]) setWins++;
                teamTotal += teamSets[i];
                oppTotal += oppSets[i];
            }

            st.GamesWon += setWins;
            st.GamesLost += (oppSets.Length - setWins);
            st.ScoreDiff += (teamTotal - oppTotal);

            if (match.WinnerId == team.TeamId) st.MatchesWon++;
        }
        statsList.Add(st);
    }

    var sorted = statsList
        .OrderByDescending(s => s.MatchesWon)
        .ThenByDescending(s => s.GamesWon)
        .ThenBy(s => s.GamesLost)
        .ThenByDescending(s => s.ScoreDiff)
        .ToList();

    int? champ = sorted.Count > 0 ? sorted[0].TeamId : null;
    int? first = sorted.Count > 1 ? sorted[1].TeamId : null;
    int? second = sorted.Count > 2 ? sorted[2].TeamId : null;

    return (champ, first, second);
}

private class RRStats
{
    public int TeamId { get; set; }
    public int MatchesWon { get; set; }
    public int GamesWon { get; set; }
    public int GamesLost { get; set; }
    public int ScoreDiff { get; set; }
}

        // ... (Keep GetCurrentUserId, GetUserAchievements, CheckAwardSettings as they were) ...
        
        [HttpGet]
        public async Task<IActionResult> GetUserAchievements(int userId)
        {
             // Paste original GetUserAchievements code here
             // (Omitted for brevity, keep exactly as in source file)
             try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return PartialView("~/Views/Competition/_EmptyAchievements.cshtml");

                var achievements = await _context.Awards
                    .Include(a => a.Team).ThenInclude(t => t!.TeamMembers)
                    .Include(a => a.Schedule)
                    .Where(a => a.Team!.TeamMembers.Any(tm => tm.UserId == userId))
                    .OrderByDescending(a => a.AwardedDate)
                    .Select(a => new AchievementItem
                    {
                        AwardId = a.AwardId,
                        AwardName = a.AwardName,
                        AwardType = a.AwardType,
                        Description = a.Description,
                        Position = a.Position,
                        PositionName = a.Position == AwardPosition.Champion ? "Champion" :
                                      a.Position == AwardPosition.FirstRunnerUp ? "1st Runner Up" : "2nd Runner Up",
                        AwardedDate = a.AwardedDate,
                        CompetitionTitle = a.Schedule!.GameName ?? "Competition",
                        TeamName = a.Team!.TeamName ?? "Unknown Team"
                    }).ToListAsync();

                var viewModel = new UserAchievementViewModel { UserId = userId, Username = user.Username ?? "User", Achievements = achievements };
                return PartialView("~/Views/Competition/_UserAchievements.cshtml", viewModel);
            }
            catch { return PartialView("~/Views/Competition/_EmptyAchievements.cshtml"); }
        }

        [HttpGet]
        public async Task<IActionResult> CheckAwardSettings(int scheduleId)
        {
            var hasAwardSettings = await _context.Awards.AnyAsync(a => a.ScheduleId == scheduleId);
            return Json(new { hasAwardSettings });
        }
    }
}