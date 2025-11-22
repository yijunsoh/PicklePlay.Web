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


        //Admin side

        [HttpGet]
        public async Task<IActionResult> GetAllReports()
        {
            try
            {
                var allReports = await _context.UserSuspensions
                    .Include(us => us.User)          // The reported user
                    .Include(us => us.ReportedBy)    // The reporter
                    .Select(us => new
                    {
                        ReportId = us.SuspensionId,
                        ReportedUserId = us.UserId,
                        ReportedUserName = us.User.Username,
                        ReportedUserEmail = us.User.Email,
                        ReporterId = us.ReportedByUserId,
                        ReporterName = us.ReportedBy.Username,
                        ReportReason = us.ReportReason,
                        CreatedAt = us.CreatedAt,
                        AdminDecision = us.AdminDecision,
                        // Get user's current status and suspension history
                        UserStatus = us.User.Status,
                        PreviousSuspensions = _context.UserSuspensions
                            .Count(s => s.UserId == us.UserId && s.AdminDecision == "Approved")
                    })
                    .OrderByDescending(us => us.CreatedAt)
                    .ToListAsync();

                return Json(new { success = true, reports = allReports });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting all reports: {ex.Message}");
                return Json(new { success = false, message = "Error loading reports" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingReports()
        {
            try
            {
                var pendingReports = await _context.UserSuspensions
                    .Where(us => us.AdminDecision == "Pending")
                    .Include(us => us.User)          // The reported user
                    .Include(us => us.ReportedBy)    // The reporter
                    .Select(us => new
                    {
                        ReportId = us.SuspensionId,
                        ReportedUserId = us.UserId,
                        ReportedUserName = us.User.Username,
                        ReportedUserEmail = us.User.Email,
                        ReporterId = us.ReportedByUserId,
                        ReporterName = us.ReportedBy.Username,
                        ReportReason = us.ReportReason,
                        CreatedAt = us.CreatedAt,
                        // Get user's current status and suspension history
                        UserStatus = us.User.Status,
                        PreviousSuspensions = _context.UserSuspensions
                            .Count(s => s.UserId == us.UserId && s.AdminDecision == "Approved")
                    })
                    .OrderByDescending(us => us.CreatedAt)
                    .ToListAsync();

                return Json(new { success = true, reports = pendingReports });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting pending reports: {ex.Message}");
                return Json(new { success = false, message = "Error loading reports" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserReportHistory(int userId)
        {
            try
            {
                var history = await _context.UserSuspensions
                    .Where(us => us.UserId == userId)
                    .Include(us => us.ReportedBy)
                    .OrderByDescending(us => us.CreatedAt)
                    .Select(us => new
                    {
                        ReportId = us.SuspensionId,
                        ReporterName = us.ReportedBy.Username,
                        ReportReason = us.ReportReason,
                        AdminDecision = us.AdminDecision,
                        CreatedAt = us.CreatedAt,
                        SuspensionStart = us.SuspensionStart,
                        SuspensionEnd = us.SuspensionEnd,
                        IsBanned = us.IsBanned,
                        RejectionReason = us.RejectionReason
                    })
                    .ToListAsync();

                return Json(new { success = true, history = history });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user history: {ex.Message}");
                return Json(new { success = false, message = "Error loading history" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ApproveReport(int reportId)
        {
            try
            {
                var report = await _context.UserSuspensions
                    .Include(us => us.User)
                    .FirstOrDefaultAsync(us => us.SuspensionId == reportId);

                if (report == null)
                {
                    return Json(new { success = false, message = "Report not found" });
                }

                var targetUser = report.User;
                var now = NowMYT();

                // Check if user has active suspension within last 30 days
                var activeSuspension = await _context.UserSuspensions
                    .Where(us => us.UserId == targetUser.UserId &&
                                us.AdminDecision == "Approved" &&
                                !us.IsBanned &&
                                us.SuspensionEnd.HasValue &&
                                us.SuspensionEnd > now)
                    .FirstOrDefaultAsync();

                string notificationMessage = "";
                string actionTaken = "";

                if (activeSuspension != null)
                {
                    // Second offense during suspension period - BAN
                    targetUser.Status = "Banned";
                    report.SuspensionStart = now;
                    report.SuspensionEnd = null; // Permanent ban
                    report.IsBanned = true;
                    actionTaken = "Banned";

                    notificationMessage = "Your account has been permanently banned due to repeated violations during your suspension period.";

                    // Also ban all other pending reports for this user
                    var otherPendingReports = await _context.UserSuspensions
                        .Where(us => us.UserId == targetUser.UserId &&
                                    us.AdminDecision == "Pending")
                        .ToListAsync();

                    foreach (var pendingReport in otherPendingReports)
                    {
                        pendingReport.AdminDecision = "Approved";
                        pendingReport.SuspensionStart = now;
                        pendingReport.SuspensionEnd = null;
                        pendingReport.IsBanned = true;
                        pendingReport.UpdatedAt = now;
                    }
                }
                else
                {
                    // First offense - SUSPEND
                    targetUser.Status = "Suspended";
                    report.SuspensionStart = now;
                    report.SuspensionEnd = now.AddDays(30); // 30-day suspended status
                    report.IsBanned = false;
                    actionTaken = "Suspended";

                    notificationMessage = $"Your account has been suspended for 30 days. You cannot login for the first 3 days. After 3 days, you may login but will remain in suspended status until {report.SuspensionEnd.Value.ToString("MMM dd, yyyy")}.";
                }

                report.AdminDecision = "Approved";
                report.UpdatedAt = now;

                // Add notification for the reported user
                _context.Notifications.Add(new Notification
                {
                    UserId = targetUser.UserId,
                    Message = notificationMessage,
                    LinkUrl = "/Home/Community", // Or wherever appropriate
                    DateCreated = now,
                    IsRead = false
                });

                await _context.SaveChangesAsync();

                string actionMessage = report.IsBanned
                    ? "User has been permanently banned (second offense during suspension)."
                    : $"User suspended for 30 days. Login blocked for 3 days.";

                return Json(new
                {
                    success = true,
                    message = $"Report approved. {actionMessage}",
                    actionTaken = actionTaken
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error approving report: {ex.Message}");
                return Json(new { success = false, message = "Error approving report" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectReport(int reportId, string? rejectionReason = null)
        {
            try
            {
                var report = await _context.UserSuspensions
                    .Include(us => us.ReportedBy) // Include reporter for notification
                    .FirstOrDefaultAsync(us => us.SuspensionId == reportId);

                if (report == null)
                {
                    return Json(new { success = false, message = "Report not found" });
                }

                var now = NowMYT();
                report.AdminDecision = "Rejected";
                report.RejectionReason = rejectionReason ?? "No reason provided";
                report.UpdatedAt = now;

                // Add notification for the reporter (the one who submitted the report)
                _context.Notifications.Add(new Notification
                {
                    UserId = report.ReportedByUserId,
                    Message = $"Your report against user has been reviewed and rejected. Reason: {report.RejectionReason}",
                    LinkUrl = "/Home/Community", // Or wherever appropriate
                    DateCreated = now,
                    IsRead = false
                });

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Report rejected successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rejecting report: {ex.Message}");
                return Json(new { success = false, message = "Error rejecting report" });
            }
        }
    }
}
