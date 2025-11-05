using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Models; // For CommunityRequest, CommunityMember, Community
using PicklePlay.Data;
using PicklePlay.ViewModels; 
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Collections.Generic; 
using System; 

namespace PicklePlay.Controllers
{
    public class CommunitiesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CommunitiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Communities/Index (Corresponds to Community.cshtml)
        public IActionResult Index()
        {
            return View("Community");
        }
        
        // GET: /Communities/GetCommunityData
        [HttpGet]
        public async Task<IActionResult> GetCommunityData()
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            int currentUserId = currentUserIdInt.GetValueOrDefault(0); 
            
            // --- 1. Fetch ALL Active Community Member Counts ---
            var communityStats = await _context.CommunityMembers
                .Where(cm => cm.Status == "Active")
                .GroupBy(cm => cm.CommunityId)
                .Select(g => new {
                    CommunityId = g.Key,
                    MemberCount = g.Count()
                })
                .ToDictionaryAsync(x => x.CommunityId, x => x.MemberCount);

            // --- 2. Fetch User's Active Community IDs ---
            var activeMemberCommunityIds = await _context.CommunityMembers
                .Where(cm => cm.UserId == currentUserId && cm.Status == "Active")
                .Select(cm => cm.CommunityId)
                .ToListAsync();

            // --- 3. Fetch Pending Requests ---
            var pendingRequests = new List<object>();
            if (currentUserId > 0) 
            {
                pendingRequests = await _context.CommunityRequests
                    .Where(cr => cr.RequestStatus == "Pending" && cr.RequestByUserId == currentUserId)
                    .Select(cr => new 
                    {
                        requestId = cr.RequestId,
                        name = cr.CommunityName,
                        location = cr.CommunityLocation,
                        type = cr.CommunityType,
                        requestDate = cr.RequestDate
                    })
                    .ToListAsync<object>();
            }

            // --- 4. Fetch Active Communities (Joined or Created by current user) ---
            var activeCommunities = await _context.CommunityMembers
                .Where(cm => cm.UserId == currentUserId && cm.Status == "Active")
                .Select(cm => new 
                {
                    id = cm.CommunityId,
                    name = cm.Community.CommunityName,
                    description = cm.Community.Description,
                    location = cm.Community.CommunityLocation,
                    type = cm.Community.CommunityType,
                    userRole = cm.CommunityRole, 
                    memberCount = communityStats.ContainsKey(cm.CommunityId) ? communityStats[cm.CommunityId] : 0, 
                    gameCount = 0,
                    icon = "city" 
                })
                .ToListAsync<object>();

            // --- 5. Fetch and Select Suggested Communities (Max 9, excluding joined/pending) ---
            var suggestedCommunitiesQuery = _context.Communities
                .Where(c => !activeMemberCommunityIds.Contains(c.CommunityId) && c.Status == "Active")
                .Select(c => new
                {
                    id = c.CommunityId,
                    name = c.CommunityName,
                    description = c.Description,
                    location = c.CommunityLocation,
                    type = c.CommunityType,
                    userRole = (string)null!, // Fixes CS8600 Warning
                    memberCount = communityStats.ContainsKey(c.CommunityId) ? communityStats[c.CommunityId] : 0, 
                    gameCount = 0,           
                    icon = "compass"         
                });

            var suggestedCommunities = await suggestedCommunitiesQuery
                .OrderBy(c => Guid.NewGuid()) // Random ordering translatable to SQL
                .Take(9) 
                .ToListAsync<object>();
            
            return Ok(new
            {
                pendingRequests = pendingRequests,
                activeCommunities = activeCommunities,
                suggestedCommunities = suggestedCommunities
            });
        }


        // POST: /Communities/JoinCommunity (Handles NEW join or REACTIVATION of Inactive membership)
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public async Task<IActionResult> JoinCommunity(int communityId)
        {
            // 1. Authentication Check
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated. Please log in to join a community." });
            }

            int userId = currentUserIdInt.Value; 

            // 2. Community Existence Check
            if (!await _context.Communities.AnyAsync(c => c.CommunityId == communityId))
            {
                return NotFound(new { success = false, message = "Community not found." });
            }
            
            // 3. Check for existing membership (Active or Inactive)
            var existingMembership = await _context.CommunityMembers
                .FirstOrDefaultAsync(cm => cm.CommunityId == communityId && cm.UserId == userId);

            if (existingMembership != null)
            {
                if (existingMembership.Status == "Active")
                {
                    return BadRequest(new { success = false, message = "You are already an active member of this community." });
                }
                
                // 4. REACTIVATE Inactive Membership
                existingMembership.Status = "Active"; 
                existingMembership.JoinDate = DateTime.UtcNow;
                _context.CommunityMembers.Update(existingMembership);
            }
            else
            {
                // 5. Create NEW Membership
                var membership = new CommunityMember
                {
                    CommunityId = communityId,
                    UserId = userId,
                    CommunityRole = "Member", 
                    Status = "Active", 
                    JoinDate = DateTime.UtcNow
                };
                _context.CommunityMembers.Add(membership);
            }

            try
            {
                await _context.SaveChangesAsync();
                
                return Ok(new
                {
                    success = true,
                    message = "Successfully joined the community!",
                    communityId = communityId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error during join: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred on the server.",
                    error = "Internal Server Error"
                });
            }
        }


        // POST: /Communities/LeaveCommunity (Updates membership status to Inactive)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveCommunity(int communityId)
        {
            // 1. Authentication Check
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated. Please log in to leave a community." });
            }

            int userId = currentUserIdInt.Value; 
            
            // 2. Find Active Membership
            var membership = await _context.CommunityMembers
                .FirstOrDefaultAsync(cm => cm.CommunityId == communityId && cm.UserId == userId && cm.Status == "Active");

            if (membership == null)
            {
                return NotFound(new { success = false, message = "Membership not found or already inactive." });
            }
            
            // 3. Update Status to Inactive
            membership.Status = "Inactive"; 
            _context.CommunityMembers.Update(membership);
            
            try
            {
                await _context.SaveChangesAsync();
                
                return Ok(new
                {
                    success = true,
                    message = "You have successfully left the community.",
                    communityId = communityId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error during leave: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred on the server.",
                    error = "Internal Server Error"
                });
            }
        }


        // POST: /Communities/SubmitCommunityRequest (No changes needed)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitCommunityRequest([FromForm] CommunityRequestSubmitViewModel model)
        {
            // 1. Check ModelState
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data submitted. Please check all required fields." });
            }

            // 2. Check user session
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated. Please log in to submit a request." });
            }

            int currentUserId = currentUserIdInt.Value; 

            try
            {
                // 3. Check for duplicates
                var existingRequest = await _context.CommunityRequests
                    .AnyAsync(cr => cr.CommunityName == model.CommunityName && cr.RequestStatus == "Pending");

                if (existingRequest)
                {
                    return BadRequest(new { success = false, message = $"A pending request for community '{model.CommunityName}' already exists." });
                }

                // 4. Create the CommunityRequest entry
                var request = new CommunityRequest
                {
                    RequestByUserId = currentUserId, 
                    CommunityName = model.CommunityName,
                    Description = model.Description,
                    CommunityLocation = model.CommunityLocation,
                    CommunityType = model.CommunityType,
                    RequestDate = DateTime.Now,
                    RequestStatus = "Pending"
                };

                _context.CommunityRequests.Add(request);
                await _context.SaveChangesAsync();

                // Return success response for the AJAX call
                return Ok(new
                {
                    success = true,
                    message = "Community request submitted successfully. It is now pending admin review.",
                    requestId = request.RequestId
                });
            }
            catch (Exception)
            {
                // Generalized 500 error handler
                return StatusCode(500, new
                {
                    success = false,
                    message = "A database error occurred while processing your request. Please try again.",
                    error = "Internal Server Error" 
                });
            }
        }
        
        // GET: /Communities/CommunityAdminDashboard (New action to display the admin view)
        public IActionResult CommunityAdminDashboard(int communityId)
        {
            // Note: In a production app, add an authorization check here 
            // (e.g., check if current user is an 'Admin' role in this specific 'communityId')
            ViewBag.CommunityId = communityId;
            return View("~/Views/Community/CommunityAdminDashboard.cshtml");
        }
    }
}