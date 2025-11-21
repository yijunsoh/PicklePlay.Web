using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;

namespace PicklePlay.Controllers
{
    public class SuspensionController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SuspensionController(ApplicationDbContext context)
        {
            _context = context;
        }

        // --- Malaysia Time Helper ---
        private DateTime NowMYT()
        {
            var myt = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, myt);
        }

        [HttpPost]
        public async Task<IActionResult> SubmitReport(int targetUserId, string reason)
        {
            try
            {
                var reporterId = HttpContext.Session.GetInt32("UserId");

                if (reporterId == null || reporterId <= 0)
                {
                    return Json(new { success = false, message = "User not logged in." });
                }

                if (targetUserId <= 0)
                {
                    return Json(new { success = false, message = "Invalid target user." });
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    return Json(new { success = false, message = "Report reason is required." });
                }

                // Prevent self-reporting
                if (targetUserId == reporterId.Value)
                {
                    return Json(new { success = false, message = "You cannot report yourself." });
                }

                // Check target user exists
                var targetUser = await _context.Users.FindAsync(targetUserId);
                if (targetUser == null)
                {
                    return Json(new { success = false, message = "User not found." });
                }

                // **LIMIT 1: Same user reporting same target within 24 hours**
                var recentDuplicateReport = await _context.UserSuspensions
                    .Where(us => us.UserId == targetUserId &&
                                 us.ReportedByUserId == reporterId.Value &&
                                 us.CreatedAt >= DateTime.UtcNow.AddHours(-24))
                    .FirstOrDefaultAsync();

                if (recentDuplicateReport != null)
                {
                    return Json(new { success = false, message = "You have already reported this user today. Please wait 24 hours before reporting them again." });
                }

                // **LIMIT 2: Overall rate limiting (3 reports per 24 hours)**
                var recentReportsCount = await _context.UserSuspensions
                    .Where(us => us.ReportedByUserId == reporterId.Value &&
                                 us.CreatedAt >= DateTime.UtcNow.AddHours(-24))
                    .CountAsync();

                if (recentReportsCount >= 3)
                {
                    return Json(new { success = false, message = "You have reached your daily reporting limit (3 reports). Please try again tomorrow." });
                }

                var now = NowMYT();

                var report = new UserSuspension
                {
                    UserId = targetUserId,
                    ReportedByUserId = reporterId.Value,
                    ReportReason = reason,
                    AdminDecision = "Pending",
                    SuspensionStart = null,
                    SuspensionEnd = null,
                    RejectionReason = null,
                    IsBanned = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.UserSuspensions.Add(report);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Report submitted successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error submitting report: {ex.Message}");
                return Json(new { success = false, message = "Something went wrong. Try again later." });
            }
        }
    }
}
