using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;

namespace PicklePlay.ViewComponents
{
    public class AchievementSectionViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public AchievementSectionViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return View("Default", new UserAchievementViewModel());
                }

                // Get all awards for teams this user is a member of
                var achievements = await _context.Awards
                    .Include(a => a.Team)
                        .ThenInclude(t => t!.TeamMembers)
                    .Include(a => a.Schedule)
                    .Where(a => a.TeamId.HasValue && 
                               a.Team!.TeamMembers.Any(tm => tm.UserId == userId))
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

                return View("Default", viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in AchievementSectionViewComponent: {ex.Message}");
                return View("Default", new UserAchievementViewModel());
            }
        }
    }
}