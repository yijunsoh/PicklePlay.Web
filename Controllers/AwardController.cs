using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

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

        

        /// <summary>
        /// Load awards/results tab for a competition
        /// </summary>
        [HttpGet]
public async Task<IActionResult> GetAwards(int scheduleId)
{
    try
    {
        var currentUserId = GetCurrentUserId();
        
        var schedule = await _context.Schedules
            .Include(s => s.Participants) // ⬅️ ADD THIS
                .ThenInclude(p => p.User) // ⬅️ ADD THIS
            .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

        if (schedule == null)
        {
            return PartialView("~/Views/Competition/_EmptyResult.cshtml", "Competition not found.");
        }

        var competition = await _context.Competitions
            .FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);

        if (competition == null)
        {
            return PartialView("~/Views/Competition/_EmptyResult.cshtml", "Competition details not found.");
        }

        // *** SAME LOGIC AS CompDetails: Get organizers and staff ***
        var organizersAndStaff = schedule.Participants
            .Where(p => p.Role == ParticipantRole.Organizer || p.Role == ParticipantRole.Staff)
            .ToList();

        // *** SAME CHECK AS CompDetails ***
        bool isOrganizer = organizersAndStaff.Any(o => o.UserId == currentUserId);

        Console.WriteLine($"Current UserId: {currentUserId}, IsOrganizer: {isOrganizer}");
        Console.WriteLine($"Organizers/Staff count: {organizersAndStaff.Count}");

        // Get existing awards
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
            IsOrganizer = isOrganizer, // ⬅️ Now using same logic as CompDetails
            HasAwards = awards.Any(),
            HasThirdPlace = competition.ThirdPlaceMatch
        };

        if (awards.Any())
        {
            // Get champion
            var champion = awards.FirstOrDefault(a => a.Position == AwardPosition.Champion);
            if (champion != null)
            {
                // ⬇️ CHECK if TeamId is not null AND > 0
                if (champion.TeamId.HasValue && champion.TeamId.Value > 0 && champion.Team != null)
                {
                    viewModel.Champion = new AwardWinnerInfo
                    {
                        TeamId = champion.TeamId.Value,
                        TeamName = champion.Team.TeamName ?? "Unknown Team",
                        PlayerNames = champion.Team.TeamMembers?
                            .Select(tm => tm.User?.Username ?? "Unknown")
                            .ToList() ?? new List<string>(),
                        AwardType = champion.AwardType,
                        AwardName = champion.AwardName,
                        Description = champion.Description,
                        AwardedDate = champion.AwardedDate,
                        Position = champion.Position
                    };
                }
                
                viewModel.AwardName = champion.AwardName;
                viewModel.AwardType = champion.AwardType;
                viewModel.Description = champion.Description;
            }

            // Get first runner up
            var firstRunner = awards.FirstOrDefault(a => a.Position == AwardPosition.FirstRunnerUp);
            if (firstRunner != null && firstRunner.TeamId.HasValue && firstRunner.TeamId.Value > 0 && firstRunner.Team != null)
            {
                viewModel.FirstRunnerUp = new AwardWinnerInfo
                {
                    TeamId = firstRunner.TeamId.Value,
                    TeamName = firstRunner.Team.TeamName ?? "Unknown Team",
                    PlayerNames = firstRunner.Team.TeamMembers?
                        .Select(tm => tm.User?.Username ?? "Unknown")
                        .ToList() ?? new List<string>(),
                    AwardType = firstRunner.AwardType,
                    AwardName = firstRunner.AwardName,
                    Description = firstRunner.Description,
                    AwardedDate = firstRunner.AwardedDate,
                    Position = firstRunner.Position
                };
            }

            // Get second runner up
            var secondRunner = awards.FirstOrDefault(a => a.Position == AwardPosition.SecondRunnerUp);
            if (secondRunner != null && secondRunner.TeamId.HasValue && secondRunner.TeamId.Value > 0 && secondRunner.Team != null)
            {
                viewModel.SecondRunnerUp = new AwardWinnerInfo
                {
                    TeamId = secondRunner.TeamId.Value,
                    TeamName = secondRunner.Team.TeamName ?? "Unknown Team",
                    PlayerNames = secondRunner.Team.TeamMembers?
                        .Select(tm => tm.User?.Username ?? "Unknown")
                        .ToList() ?? new List<string>(),
                    AwardType = secondRunner.AwardType,
                    AwardName = secondRunner.AwardName,
                    Description = secondRunner.Description,
                    AwardedDate = secondRunner.AwardedDate,
                    Position = secondRunner.Position
                };
            }
        }

        return PartialView("~/Views/Competition/_ResultsView.cshtml", viewModel);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in GetAwards: {ex.Message}");
        return PartialView("~/Views/Competition/_EmptyResult.cshtml", $"Error loading results: {ex.Message}");
    }
}

        /// <summary>
        /// Create/Update awards for competition
        /// </summary>
        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SetAwards(int scheduleId, string awardName, AwardType awardType, string? description)
{
    try
    {
        var schedule = await _context.Schedules
            .Include(s => s.Participants)
            .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

        if (schedule == null)
        {
            return Json(new { success = false, message = "Competition not found." });
        }

        var competition = await _context.Competitions
            .FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);

        if (competition == null)
        {
            return Json(new { success = false, message = "Competition not found." });
        }

        // Check if awards already exist
        var existingAwardConfig = await _context.Awards
            .FirstOrDefaultAsync(a => a.ScheduleId == scheduleId && a.Position == AwardPosition.Champion);

        if (existingAwardConfig != null)
        {
            // Update existing awards
            var allAwards = await _context.Awards
                .Where(a => a.ScheduleId == scheduleId)
                .ToListAsync();

            foreach (var award in allAwards)
            {
                award.AwardName = awardName;
                award.AwardType = awardType;
                award.Description = description;
            }

            _context.Awards.UpdateRange(allAwards);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Award settings updated successfully!" });
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

        int? championTeamId = finalMatch?.WinnerId;
        int? firstRunnerUpTeamId = null;
        int? secondRunnerUpTeamId = thirdPlaceMatch?.WinnerId;

        if (championTeamId.HasValue && finalMatch != null)
        {
            firstRunnerUpTeamId = (finalMatch.Team1Id == championTeamId) ? finalMatch.Team2Id : finalMatch.Team1Id;
        }

        // Create awards (use 1 as placeholder for SetByUserId)
        var newAwards = new List<Award>
        {
            new Award
            {
                ScheduleId = scheduleId,
                AwardName = awardName,
                AwardType = awardType,
                Description = description,
                Position = AwardPosition.Champion,
                TeamId = championTeamId,
                AwardedDate = DateTime.UtcNow,
                SetByUserId = 1 // Placeholder
            },
            new Award
            {
                ScheduleId = scheduleId,
                AwardName = awardName,
                AwardType = awardType,
                Description = description,
                Position = AwardPosition.FirstRunnerUp,
                TeamId = firstRunnerUpTeamId,
                AwardedDate = DateTime.UtcNow,
                SetByUserId = 1 // Placeholder
            }
        };

        if (competition.ThirdPlaceMatch)
        {
            newAwards.Add(new Award
            {
                ScheduleId = scheduleId,
                AwardName = awardName,
                AwardType = awardType,
                Description = description,
                Position = AwardPosition.SecondRunnerUp,
                TeamId = secondRunnerUpTeamId,
                AwardedDate = DateTime.UtcNow,
                SetByUserId = 1 // Placeholder
            });
        }

        await _context.Awards.AddRangeAsync(newAwards);
        await _context.SaveChangesAsync();

        string message = championTeamId.HasValue 
            ? "Awards created successfully with winners!" 
            : "Award settings saved! Winners will appear once matches are completed.";

        Console.WriteLine($"Awards saved successfully: {newAwards.Count} awards created");

        return Json(new { success = true, message = message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in SetAwards: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        return Json(new { success = false, message = $"Error: {ex.Message}" });
    }
}

        /// <summary>
        /// Get user achievements for profile page
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUserAchievements(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return PartialView("~/Views/Competition/_EmptyAchievements.cshtml");
                }

                // Get all awards for teams this user is a member of
                var achievements = await _context.Awards
                    .Include(a => a.Team)
                        .ThenInclude(t => t!.TeamMembers)
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
                    })
                    .ToListAsync();

                var viewModel = new UserAchievementViewModel
                {
                    UserId = userId,
                    Username = user.Username ?? "User",
                    Achievements = achievements
                };

                return PartialView("~/Views/Competition/_UserAchievements.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in GetUserAchievements: {ex.Message}");
                return PartialView("~/Views/Competition/_EmptyAchievements.cshtml");
            }
        }


        [HttpGet]
public async Task<IActionResult> CheckAwardSettings(int scheduleId)
{
    try
    {
        var hasAwardSettings = await _context.Awards
            .AnyAsync(a => a.ScheduleId == scheduleId);

        Console.WriteLine($"CheckAwardSettings for schedule {scheduleId}: {hasAwardSettings}");

        return Json(new { hasAwardSettings });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in CheckAwardSettings: {ex.Message}");
        return Json(new { hasAwardSettings = false });
    }
}

[HttpPost]
public async Task<IActionResult> UpdateAwardWinners(int scheduleId)
{
    try
    {
        var competition = await _context.Competitions
            .FirstOrDefaultAsync(c => c.ScheduleId == scheduleId);

        if (competition == null)
        {
            return Json(new { success = false, message = "Competition not found." });
        }

        // Get existing awards
        var awards = await _context.Awards
            .Where(a => a.ScheduleId == scheduleId)
            .ToListAsync();

        if (!awards.Any())
        {
            return Json(new { success = false, message = "No awards configured." });
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
            firstRunnerUpTeamId = (finalMatch.Team1Id == championTeamId) ? finalMatch.Team2Id : finalMatch.Team1Id;
        }

        // Update awards with winners
        var championAward = awards.FirstOrDefault(a => a.Position == AwardPosition.Champion);
        if (championAward != null && championTeamId.HasValue)
        {
            championAward.TeamId = championTeamId.Value;
        }

        var firstRunnerAward = awards.FirstOrDefault(a => a.Position == AwardPosition.FirstRunnerUp);
        if (firstRunnerAward != null && firstRunnerUpTeamId.HasValue)
        {
            firstRunnerAward.TeamId = firstRunnerUpTeamId.Value;
        }

        var secondRunnerAward = awards.FirstOrDefault(a => a.Position == AwardPosition.SecondRunnerUp);
        if (secondRunnerAward != null && secondRunnerUpTeamId.HasValue)
        {
            secondRunnerAward.TeamId = secondRunnerUpTeamId.Value;
        }

        _context.Awards.UpdateRange(awards);
        await _context.SaveChangesAsync();

        Console.WriteLine($"✓ Award winners updated: Champion={championTeamId}, 1st={firstRunnerUpTeamId}, 2nd={secondRunnerUpTeamId}");

        return Json(new { 
            success = true, 
            message = "Award winners updated successfully!",
            championTeamId,
            firstRunnerUpTeamId,
            secondRunnerUpTeamId
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR in UpdateAwardWinners: {ex.Message}");
        return Json(new { success = false, message = $"Error: {ex.Message}" });
    }
}
    }
}