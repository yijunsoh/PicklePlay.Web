using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.ViewModels;
using PicklePlay.Models;

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

    public IActionResult TransactionLog()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
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
        return View();
    }

    public IActionResult Refund()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }
}