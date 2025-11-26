using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.ViewModels;
using PicklePlay.Models;
using System.Text;

public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;

    public AdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    public static DateTime NowMYT()
    {
        var myt = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, myt);
    }
    // GET: /Admin/Dashboard
    public async Task<IActionResult> AdminDashboard()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        var viewModel = new AdminDashboardViewModel();

        try
        {
            // Get basic stats
            viewModel.TotalUsers = await _context.Users.CountAsync();
            viewModel.ActiveCommunities = await _context.Communities
                .Where(c => c.Status == "Active")
                .CountAsync();

            // Get individual pending counts
            viewModel.PendingCommunityRequestsCount = await _context.CommunityRequests
                .Where(cr => cr.RequestStatus == "Pending")
                .CountAsync();

            viewModel.PendingEscrowDisputesCount = await _context.EscrowDisputes
                .Where(ed => ed.AdminDecision == "Pending")
                .CountAsync();

            viewModel.PendingRefundRequestsCount = await _context.RefundRequests
                .Where(rr => rr.AdminDecision == "Pending")
                .CountAsync();

            viewModel.PendingSuspensionRequestsCount = await _context.UserSuspensions
                .Where(us => us.AdminDecision == "Pending")
                .CountAsync();

            // Calculate TOTAL pending requests (sum of all types)
            viewModel.TotalPendingRequests = viewModel.PendingCommunityRequestsCount +
                                           viewModel.PendingEscrowDisputesCount +
                                           viewModel.PendingRefundRequestsCount +
                                           viewModel.PendingSuspensionRequestsCount;

            // Total transactions amount in RM (completed payments only)
            viewModel.TotalTransactions = await _context.Transactions
                .Where(t => t.PaymentStatus == "Completed" && t.TransactionType != "Escrow_Hold")
                .SumAsync(t => t.Amount);

            // Get recent activities (last 7 days)
            var sevenDaysAgo = NowMYT().AddDays(-7);

            var recentActivities = new List<RecentActivityViewModel>();

            // Recent community requests
            var recentCommunityRequests = await _context.CommunityRequests
                .Where(cr => cr.RequestDate >= sevenDaysAgo)
                .Include(cr => cr.RequestByUser)
                .OrderByDescending(cr => cr.RequestDate)
                .Take(10)
                .Select(cr => new RecentActivityViewModel
                {
                    Type = "Community Request",
                    Description = $"New community request: {cr.CommunityName}",
                    CreatedAt = cr.RequestDate,
                    Username = cr.RequestByUser.Username,
                    Status = cr.RequestStatus
                })
                .ToListAsync();

            recentActivities.AddRange(recentCommunityRequests);

            // Recent disputes
            var recentDisputes = await _context.EscrowDisputes
                .Where(ed => ed.CreatedAt >= sevenDaysAgo)
                .Include(ed => ed.RaisedByUser)
                .OrderByDescending(ed => ed.CreatedAt)
                .Take(10)
                .Select(ed => new RecentActivityViewModel
                {
                    Type = "Escrow Dispute",
                    Description = $"Dispute raised for schedule",
                    CreatedAt = ed.CreatedAt,
                    Username = ed.RaisedByUser.Username,
                    Status = ed.AdminDecision
                })
                .ToListAsync();

            recentActivities.AddRange(recentDisputes);

            // Recent refund requests
            var recentRefunds = await _context.RefundRequests
                .Where(rr => rr.CreatedAt >= sevenDaysAgo)
                .Include(rr => rr.User)
                .OrderByDescending(rr => rr.CreatedAt)
                .Take(10)
                .Select(rr => new RecentActivityViewModel
                {
                    Type = "Refund Request",
                    Description = $"Refund request submitted",
                    CreatedAt = rr.CreatedAt,
                    Username = rr.User.Username,
                    Status = rr.AdminDecision
                })
                .ToListAsync();

            recentActivities.AddRange(recentRefunds);

            viewModel.RecentActivities = recentActivities
                .OrderByDescending(ra => ra.CreatedAt)
                .Take(8)
                .ToList();

            // Get user growth data for chart (last 6 months) - SIMPLIFIED
            var sixMonthsAgo = NowMYT().AddMonths(-6);

            var monthlyGrowth = await _context.Users
                .Where(u => u.CreatedDate >= sixMonthsAgo)
                .GroupBy(u => new { Year = u.CreatedDate.Year, Month = u.CreatedDate.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    NewUsers = g.Count()
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            // Calculate cumulative totals for tooltips
            var allUsersBeforePeriod = await _context.Users
                .Where(u => u.CreatedDate < sixMonthsAgo)
                .CountAsync();

            int cumulativeTotal = allUsersBeforePeriod;
            viewModel.MonthlyTotalUsers = new Dictionary<string, int>();

            viewModel.MonthlyUserGrowth = monthlyGrowth
                .Select(m =>
                {
                    var monthKey = new DateTime(m.Year, m.Month, 1).ToString("MMM yyyy");
                    cumulativeTotal += m.NewUsers;
                    viewModel.MonthlyTotalUsers[monthKey] = cumulativeTotal;

                    return new MonthlyUserGrowth
                    {
                        Month = monthKey,
                        NewUsers = m.NewUsers
                    };
                })
                .ToList();
        }
        catch (Exception ex)
        {
            // Log error but don't crash the page
            Console.WriteLine($"Error loading dashboard data: {ex.Message}");
        }

        return View("AdminDashboard", viewModel);
    }

    public IActionResult SuspendList()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    // GET: /Admin/TransactionLog
    public async Task<IActionResult> TransactionLog()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        // Get filter parameters
        var startDate = Request.Query["startDate"].FirstOrDefault();
        var endDate = Request.Query["endDate"].FirstOrDefault();
        var transactionType = Request.Query["transactionType"].FirstOrDefault();
        var paymentStatus = Request.Query["paymentStatus"].FirstOrDefault();
        var paymentMethod = Request.Query["paymentMethod"].FirstOrDefault();

        // Build query
        var query = _context.Transactions
            .Include(t => t.Wallet)
                .ThenInclude(w => w.User)
            .Include(t => t.Escrow)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
        {
            query = query.Where(t => t.CreatedAt >= start);
        }

        if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
        {
            query = query.Where(t => t.CreatedAt <= end.AddDays(1)); // Include the entire end date
        }

        if (!string.IsNullOrEmpty(transactionType) && transactionType != "All")
        {
            query = query.Where(t => t.TransactionType == transactionType);
        }

        if (!string.IsNullOrEmpty(paymentStatus) && paymentStatus != "All")
        {
            query = query.Where(t => t.PaymentStatus == paymentStatus);
        }

        if (!string.IsNullOrEmpty(paymentMethod) && paymentMethod != "All")
        {
            query = query.Where(t => t.PaymentMethod == paymentMethod);
        }

        // Get transactions ordered by latest first
        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TransactionLogViewModel
            {
                TransactionId = t.TransactionId,
                UserId = t.Wallet.UserId,
                Username = t.Wallet.User.Username,
                Email = t.Wallet.User.Email,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                PaymentMethod = t.PaymentMethod,
                PaymentStatus = t.PaymentStatus,
                PaymentGatewayId = t.PaymentGatewayId,
                CardLastFour = t.CardLastFour,
                CreatedAt = t.CreatedAt,
                PaymentCompletedAt = t.PaymentCompletedAt,
                WalletId = t.WalletId,
                EscrowId = t.EscrowId,
                IsEscrowRelated = t.EscrowId != null
            })
            .ToListAsync();

        // Calculate summary statistics FROM FILTERED DATA
        var totalTransactions = transactions.Count;
        var totalAmount = transactions.Sum(t => t.Amount);
        var successfulTransactions = transactions.Count(t => t.PaymentStatus == "Completed");
        var pendingTransactions = transactions.Count(t => t.PaymentStatus == "Pending");
        var failedTransactions = transactions.Count(t => t.PaymentStatus == "Failed");

        var viewModel = new TransactionLogMainViewModel
        {
            Transactions = transactions,
            Filter = new TransactionFilterViewModel
            {
                StartDate = startDate,
                EndDate = endDate,
                TransactionType = transactionType,
                PaymentStatus = paymentStatus,
                PaymentMethod = paymentMethod
            },
            Summary = new TransactionSummaryViewModel
            {
                TotalTransactions = totalTransactions,
                TotalAmount = totalAmount,
                SuccessfulTransactions = successfulTransactions,
                PendingTransactions = pendingTransactions,
                FailedTransactions = failedTransactions
            }
        };

        return View(viewModel);
    }

    // GET: /Admin/GetTransactionDetails
    [HttpGet]
    public async Task<IActionResult> GetTransactionDetails(int id)
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Unauthorized(new { success = false, message = "Unauthorized" });
        }

        var transaction = await _context.Transactions
            .Include(t => t.Wallet)
                .ThenInclude(w => w.User)
            .Include(t => t.Escrow)
            .FirstOrDefaultAsync(t => t.TransactionId == id);

        if (transaction == null)
        {
            return NotFound(new { success = false, message = "Transaction not found" });
        }

        var details = new
        {
            TransactionId = transaction.TransactionId,
            UserId = transaction.Wallet.UserId,
            Username = transaction.Wallet.User.Username,
            Email = transaction.Wallet.User.Email,
            TransactionType = transaction.TransactionType,
            Amount = transaction.Amount,
            PaymentMethod = transaction.PaymentMethod,
            PaymentStatus = transaction.PaymentStatus,
            PaymentGatewayId = transaction.PaymentGatewayId,
            CardLastFour = transaction.CardLastFour,
            CreatedAt = transaction.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            PaymentCompletedAt = transaction.PaymentCompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            WalletId = transaction.WalletId,
            EscrowId = transaction.EscrowId
        };

        return Ok(new { success = true, data = details });
    }

    // GET: /Admin/ExportTransactions
    [HttpGet]
    public async Task<IActionResult> ExportTransactions()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Unauthorized(new { success = false, message = "Unauthorized" });
        }

        try
        {
            // Get filter parameters from query string
            var startDate = Request.Query["startDate"].FirstOrDefault();
            var endDate = Request.Query["endDate"].FirstOrDefault();
            var transactionType = Request.Query["transactionType"].FirstOrDefault();
            var paymentStatus = Request.Query["paymentStatus"].FirstOrDefault();
            var paymentMethod = Request.Query["paymentMethod"].FirstOrDefault();

            // Build query (same as TransactionLog action)
            var query = _context.Transactions
                .Include(t => t.Wallet)
                    .ThenInclude(w => w.User)
                .AsQueryable();

            // Apply filters (same logic as TransactionLog action)
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
            {
                query = query.Where(t => t.CreatedAt >= start);
            }

            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
            {
                query = query.Where(t => t.CreatedAt <= end.AddDays(1));
            }

            if (!string.IsNullOrEmpty(transactionType) && transactionType != "All")
            {
                query = query.Where(t => t.TransactionType == transactionType);
            }

            if (!string.IsNullOrEmpty(paymentStatus) && paymentStatus != "All")
            {
                query = query.Where(t => t.PaymentStatus == paymentStatus);
            }

            if (!string.IsNullOrEmpty(paymentMethod) && paymentMethod != "All")
            {
                query = query.Where(t => t.PaymentMethod == paymentMethod);
            }

            var transactions = await query
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.TransactionId,
                    t.Wallet.User.Username,
                    t.Wallet.User.Email,
                    t.TransactionType,
                    t.Amount,
                    t.PaymentMethod,
                    t.PaymentStatus,
                    t.PaymentGatewayId,
                    t.CardLastFour,
                    t.CreatedAt,
                    t.PaymentCompletedAt
                })
                .ToListAsync();

            // Create CSV content
            var csv = new StringBuilder();
            csv.AppendLine("TransactionID,Username,Email,Type,Amount,PaymentMethod,Status,PaymentGatewayID,CardLastFour,CreatedAt,CompletedAt");

            foreach (var t in transactions)
            {
                csv.AppendLine($"\"{t.TransactionId}\",\"{t.Username}\",\"{t.Email}\",\"{t.TransactionType}\",\"{t.Amount}\",\"{t.PaymentMethod}\",\"{t.PaymentStatus}\",\"{t.PaymentGatewayId ?? "N/A"}\",\"{t.CardLastFour ?? "N/A"}\",\"{t.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{t.PaymentCompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}\"");
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"transactions_{NowMYT():yyyyMMdd_HHmmss}.csv");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting transactions: {ex.Message}");
            return StatusCode(500, new { success = false, message = "Error exporting transactions" });
        }
    }

    // GET: /Admin/CommunityRequests
    public async Task<IActionResult> CommunityRequests()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        // Fetch only Pending requests, joining with User to get the requester's name (Assuming User model exists and is linked)
        var requests = await _context.CommunityRequests
            .Where(cr => cr.RequestStatus == "Pending")
            .Include(cr => cr.RequestByUser) // Include the User navigation property
            .Select(cr => new CommunityRequestAdminViewModel
            {
                RequestId = cr.RequestId,
                CommunityName = cr.CommunityName,
                RequesterUsername = cr.RequestByUser.Username, // Assuming User model has a Username property
                RequestDate = cr.RequestDate,
                Description = cr.Description,
                CommunityLocation = cr.CommunityLocation,
                CommunityType = cr.CommunityType
            })
            .ToListAsync();

        return View(requests);
    }

    // POST: /Admin/AcceptCommunityRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptCommunityRequest(int requestId)
    {
        var request = await _context.CommunityRequests
            .FirstOrDefaultAsync(cr => cr.RequestId == requestId && cr.RequestStatus == "Pending");

        if (request == null)
        {
            return NotFound(new { success = false, message = "Pending request not found." });
        }

        var nowMYT = NowMYT();
        // 1. Create the new Community
        var community = new Community
        {
            CommunityName = request.CommunityName,
            Description = request.Description,
            CreateByUserId = request.RequestByUserId,
            CommunityLocation = request.CommunityLocation,
            CommunityType = request.CommunityType,
            CreatedDate = nowMYT,
            Status = "Active" // Set as active
        };

        _context.Communities.Add(community);
        await _context.SaveChangesAsync(); // Save to get the new CommunityId

        // 2. Add the requester as the first CommunityMember (Admin)
        var member = new CommunityMember
        {
            CommunityId = community.CommunityId,
            UserId = request.RequestByUserId,
            CommunityRole = "Admin", // Set initial role
            Status = "Active",
            JoinDate = nowMYT
        };

        _context.CommunityMembers.Add(member);

        // 3. Update the Request Status
        request.RequestStatus = "Approved";

        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = $"Community '{community.CommunityName}' has been created and approved." });
    }

    // POST: /Admin/RejectCommunityRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectCommunityRequest(int requestId, [FromForm] string reason)
    {
        var request = await _context.CommunityRequests
            .FirstOrDefaultAsync(cr => cr.RequestId == requestId && cr.RequestStatus == "Pending");

        if (request == null)
        {
            return NotFound(new { success = false, message = "Pending request not found." });
        }

        // 1. Update the Request Status
        request.RequestStatus = "Rejected";
        // Note: 'reason' is not saved in the provided CommunityRequest model, but is available here.

        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = $"Community request for '{request.CommunityName}' rejected." });
    }

    // ... (Other Admin Actions)

    public IActionResult EscrowDashboard()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult EscrowTransaction()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return RedirectToAction("Index", "EscrowAdmin");
    }
    public async Task<IActionResult> EscrowDispute()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        var disputes = await _context.EscrowDisputes
            .Include(d => d.RaisedByUser)
            .Include(d => d.Schedule)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return View("~/Views/Admin/EscrowDispute.cshtml", disputes);
    }
    public async Task<IActionResult> Refund()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        var refundRequests = await _context.RefundRequests
            .Include(r => r.User)           // user who reported
            .Include(r => r.Escrow)         // include escrow details
            .ThenInclude(e => e!.Schedule)   // include schedule info
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return View("~/Views/Admin/Refund.cshtml", refundRequests);
    }

    // GET: /Admin/InactiveCommunities
    public async Task<IActionResult> InactiveCommunities(int? inactiveDays = 0, string search = "", string statusFilter = "all")
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        if (inactiveDays == null)
        {
            inactiveDays = 0;
        }

        IQueryable<Community> query = _context.Communities
            .Include(c => c.Creator)
            .Include(c => c.Memberships);

        // Apply search filter
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(c => c.CommunityName.Contains(search) ||
                                    c.CommunityLocation!.Contains(search));
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
        {
            query = query.Where(c => c.Status == statusFilter);
        }

        // If inactiveDays is 0, show ALL communities (including deleted)
        // Otherwise, show only active communities inactive for more than inactiveDays
        if (inactiveDays > 0)
        {
            var cutoffDate = NowMYT().AddDays(-inactiveDays.Value);
            query = query.Where(c => c.Status == "Active" &&
                                   (c.LastActivityDate == null || c.LastActivityDate <= cutoffDate));
        }

        // Always sort: Active first, then by name
        query = query.OrderByDescending(c => c.Status == "Active")
                    .ThenBy(c => c.Status)
                    .ThenBy(c => c.CommunityName);

        var communities = await query.ToListAsync();

        ViewData["InactiveDays"] = inactiveDays;
        ViewData["Search"] = search;
        ViewData["StatusFilter"] = statusFilter;
        return View("~/Views/Admin/InactiveCommunities.cshtml", communities);
    }

    // POST: /Admin/DeleteInactiveCommunity
    [HttpPost]
    public async Task<IActionResult> DeleteInactiveCommunity(int communityId, string? reason)
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Json(new { success = false, message = "Unauthorized" });
        }

        try
        {
            var community = await _context.Communities
                .Include(c => c.Memberships) // Include memberships to update them
                .FirstOrDefaultAsync(c => c.CommunityId == communityId);

            if (community == null)
            {
                return Json(new { success = false, message = "Community not found" });
            }

            var currentUserId = HttpContext.Session.GetInt32("UserId");

            // Store original name before modifying
            string originalName = community.CommunityName;

            // Generate unique suffix with timestamp and random characters
            var nowMYT = NowMYT();
            string timestamp = nowMYT.ToString("yyyyMMddHHmmss");
            string randomSuffix = Guid.NewGuid().ToString("N")[..6];

            // Update community name with deletion marker
            community.CommunityName = $"[{originalName}]_deleted_{timestamp}_{randomSuffix}";
            community.Status = "Deleted";
            community.DeletionReason = reason ?? "Deleted by admin - inactive community";
            community.DeletedByUserId = currentUserId;
            community.DeletionDate = nowMYT;

            // --- UPDATE ALL COMMUNITY MEMBERS TO INACTIVE ---
            foreach (var member in community.Memberships)
            {
                member.Status = "Inactive";
                _context.CommunityMembers.Update(member);
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Community deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // GET: /Admin/UpdateAllCommunityLastActivity
    [HttpGet]
    public async Task<IActionResult> UpdateAllCommunityLastActivity()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        try
        {
            var communities = await _context.Communities
                .Where(c => c.Status == "Active")
                .ToListAsync();

            int updatedCount = 0;

            foreach (var community in communities)
            {
                var latestSchedule = await _context.Schedules
                    .Where(s => s.CommunityId == community.CommunityId)
                    .OrderByDescending(s => s.StartTime)
                    .FirstOrDefaultAsync();

                if (latestSchedule != null && latestSchedule.StartTime.HasValue)
                {
                    community.LastActivityDate = latestSchedule.StartTime;
                    updatedCount++;
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Updated {updatedCount} communities with their latest activity dates.";
            return RedirectToAction("InactiveCommunities");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error: {ex.Message}";
            return RedirectToAction("InactiveCommunities");
        }
    }

    // POST: /Admin/UpdateCommunityLastActivity
    [HttpPost]
    public async Task<IActionResult> UpdateCommunityLastActivity(int communityId)
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return Json(new { success = false, message = "Unauthorized" });
        }

        try
        {
            var community = await _context.Communities.FindAsync(communityId);
            if (community == null)
            {
                return Json(new { success = false, message = "Community not found" });
            }

            // Get the latest schedule/competition activity for this community
            var latestSchedule = await _context.Schedules
                .Where(s => s.CommunityId == communityId)
                .OrderByDescending(s => s.StartTime)
                .FirstOrDefaultAsync();

            if (latestSchedule != null)
            {
                community.LastActivityDate = latestSchedule.StartTime;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Last activity date updated", lastActivityDate = latestSchedule.StartTime });
            }

            return Json(new { success = false, message = "No schedule found for this community" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // Add this method to your AdminController.cs
    public async Task<IActionResult> GrowthReport()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }

        try
        {
            // Fetch real data only - no sample data
            var sixMonthsAgo = NowMYT().AddMonths(-6);

            var monthlyGrowth = await _context.Users
                .Where(u => u.CreatedDate >= sixMonthsAgo)
                .GroupBy(u => new { Year = u.CreatedDate.Year, Month = u.CreatedDate.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    NewUsers = g.Count()
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            List<MonthlyGrowthData> monthlyData = new List<MonthlyGrowthData>();

            if (monthlyGrowth.Any())
            {
                // Use real data only
                monthlyData = monthlyGrowth.Select(g => new MonthlyGrowthData
                {
                    Month = new DateTime(g.Year, g.Month, 1).ToString("MMM"),
                    Users = g.NewUsers
                }).ToList();
            }

            // Calculate summary with real data only
            var peakGrowth = monthlyData.Any() ? monthlyData.OrderByDescending(m => m.Users).First() : null;
            var lowestGrowth = monthlyData.Any() ? monthlyData.OrderBy(m => m.Users).First() : null;
            var totalUsersGained = monthlyData.Sum(m => m.Users);
            var averageMonthlyGrowth = monthlyData.Any() ? (int)monthlyData.Average(m => m.Users) : 0;

            var viewModel = new GrowthReportViewModel
            {
                ReportTitle = "Yearly User Growth (2025)",
                MonthlyData = monthlyData,
                Summary = new ReportSummary
                {
                    PeakGrowthMonth = peakGrowth?.Month ?? "No data",
                    PeakGrowthValue = peakGrowth?.Users ?? 0,
                    LowestGrowthMonth = lowestGrowth?.Month ?? "No data",
                    LowestGrowthValue = lowestGrowth?.Users ?? 0,
                    TotalUsersGained = totalUsersGained,
                    AverageMonthlyGrowth = averageMonthlyGrowth,
                    Period = "6 months"
                }
            };

            return View("GrowthReport", viewModel);
        }
        catch (Exception)
        {
            // Return empty data instead of sample data
            var viewModel = new GrowthReportViewModel
            {
                ReportTitle = "Yearly User Growth (2025)",
                MonthlyData = new List<MonthlyGrowthData>(),
                Summary = new ReportSummary
                {
                    PeakGrowthMonth = "No data",
                    PeakGrowthValue = 0,
                    LowestGrowthMonth = "No data",
                    LowestGrowthValue = 0,
                    TotalUsersGained = 0,
                    AverageMonthlyGrowth = 0,
                    Period = "6 months"
                }
            };

            return View("GrowthReport", viewModel);
        }
    }

}
