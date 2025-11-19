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

    public IActionResult AdminDashboard()
    {
        // Manual session check instead of [Authorize]
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
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
            return File(bytes, "text/csv", $"transactions_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
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

        // 1. Create the new Community
        var community = new Community
        {
            CommunityName = request.CommunityName,
            Description = request.Description,
            CreateByUserId = request.RequestByUserId,
            CommunityLocation = request.CommunityLocation,
            CommunityType = request.CommunityType,
            CreatedDate = DateTime.UtcNow,
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
            JoinDate = DateTime.UtcNow
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
    public IActionResult InactiveCommunities()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

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

}