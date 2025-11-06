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
                .Select(g => new
                {
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
        // POST: /Communities/SubmitCommunityRequest
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitCommunityRequest([FromForm] CommunityRequestSubmitViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid data submitted. Please check all required fields." });

            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
                return Unauthorized(new { success = false, message = "User not authenticated. Please log in to submit a request." });

            // --- Normalize name ---
            var name = (model.CommunityName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { success = false, message = "Community name is required." });

            // 1) Block if same name already exists in Communities (accepted/active community)
            //    (Your MySQL collation likely already compares case-insensitively; equality is fine.)
            var existsCommunity = await _context.Communities
                .AnyAsync(c => c.CommunityName == name);

            if (existsCommunity)
                return Conflict(new
                {
                    success = false,
                    message = $"A community named '{name}' already exists. Please choose a different name."
                });

            // 2) Block if same name already has a Pending request (by anyone)
            var existsPendingRequest = await _context.CommunityRequests
                .AnyAsync(cr => cr.CommunityName == name && cr.RequestStatus == "Pending");

            if (existsPendingRequest)
                return Conflict(new
                {
                    success = false,
                    message = $"A pending request for '{name}' already exists. Please pick another name or wait for the review."
                });

            // Passed the guards — create the request
            var currentUserId = currentUserIdInt.Value;

            try
            {
                var request = new CommunityRequest
                {
                    RequestByUserId = currentUserId,
                    CommunityName = name,
                    Description = model.Description,
                    CommunityLocation = model.CommunityLocation,
                    CommunityType = model.CommunityType,
                    RequestDate = DateTime.Now,
                    RequestStatus = "Pending"
                };

                _context.CommunityRequests.Add(request);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Community request submitted successfully. It is now pending admin review.",
                    requestId = request.RequestId
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "A database error occurred while processing your request. Please try again.",
                    error = "Internal Server Error"
                });
            }
        }


        // // GET: /Communities/CommunityAdminDashboard (New action to display the admin view)
        // public IActionResult CommunityAdminDashboard(int communityId)
        // {
        //     // Note: In a production app, add an authorization check here 
        //     // (e.g., check if current user is an 'Admin' role in this specific 'communityId')
        //     ViewBag.CommunityId = communityId;
        //     return View("~/Views/Community/CommunityAdminDashboard.cshtml");
        // }

        // GET: /Communities/CommunityAdminDashboard
        [HttpGet]
        public async Task<IActionResult> CommunityAdminDashboard(int communityId)
        {
            var c = await _context.Communities
                .Include(x => x.Creator)
                .Include(x => x.Memberships).ThenInclude(m => m.User)
                .Include(x => x.BlockedUsers)
                .Include(x => x.Announcements).ThenInclude(a => a.Poster)
                .FirstOrDefaultAsync(x => x.CommunityId == communityId);

            if (c == null) return NotFound();

            // ---- Resolve current user id robustly (Session -> Claims -> Username lookup) ----
            int currentUserId =
                HttpContext.Session.GetInt32("UserId") ?? 0;

            if (currentUserId == 0)
            {
                var claimId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(claimId, out var fromClaim))
                    currentUserId = fromClaim;
            }
            if (currentUserId == 0)
            {
                // Optional: try username lookup if your session stores it or Identity.Name is set
                var uname = HttpContext.Session.GetString("Username") ?? User?.Identity?.Name;
                if (!string.IsNullOrWhiteSpace(uname))
                {
                    // Adjust entity/prop names if different
                    currentUserId = await _context.Users
                        .Where(u => u.Username == uname)
                        .Select(u => u.UserId)
                        .FirstOrDefaultAsync();
                }
            }

            var vm = new CommunityAdminDashboardViewModel
            {
                CommunityId = c.CommunityId,
                CommunityName = c.CommunityName,
                Description = c.Description,
                CommunityLocation = c.CommunityLocation,
                CommunityType = c.CommunityType,
                Status = c.Status,
                CreatedDate = c.CreatedDate,
                CommunityPic = c.CommunityPic,

                CreatedByUserId = c.CreateByUserId,
                CreatedByUserName = c.Creator?.Username ?? $"User #{c.CreateByUserId}",

                MemberCountActive = c.Memberships.Count(m => m.Status == "Active"),
                MemberCountTotal = c.Memberships.Count(),
                AdminCount = c.Memberships.Count(m => m.Status == "Active" && m.CommunityRole == "Admin"),
                ModeratorCount = c.Memberships.Count(m => m.Status == "Active" && m.CommunityRole == "Moderator"),
                BlockedUserCount = c.BlockedUsers?.Count ?? 0,
                AnnouncementCount = c.Announcements?.Count ?? 0,

                LatestAnnouncements = c.Announcements?
                    .OrderByDescending(a => a.PostDate)
                    .Take(5)
                    .Select(a => new CommunityAdminDashboardViewModel.AnnouncementItem
                    {
                        Id = a.AnnouncementId,
                        Title = a.Title,
                        PostDate = a.PostDate,
                        PosterUserId = a.PosterUserId,
                        PosterName = a.Poster?.Username ?? $"User #{a.PosterUserId}"
                    }).ToList() ?? new List<CommunityAdminDashboardViewModel.AnnouncementItem>(),

                Members = c.Memberships
                    .OrderByDescending(m => m.JoinDate)
                    .Select(m => new CommunityAdminDashboardViewModel.MemberItem
                    {
                        UserId = m.UserId,
                        UserName = m.User?.Username ?? $"User #{m.UserId}",
                        Role = m.CommunityRole,
                        Status = m.Status,
                        JoinDate = m.JoinDate
                    }).ToList(),

                JoinRequests = c.Memberships
                    .Where(m => m.Status == "Pending")
                    .OrderByDescending(m => m.JoinDate)
                    .Select(m => new CommunityAdminDashboardViewModel.JoinRequestItem
                    {
                        RequestId = m.MemberId,
                        UserId = m.UserId,
                        Username = m.User?.Username ?? $"User #{m.UserId}",
                        RequestedDate = m.JoinDate
                    }).ToList()
            };

            // ---- Correctly determine viewer's role ----
            var myMembership = c.Memberships.FirstOrDefault(m => m.UserId == currentUserId);
            if (myMembership != null)
            {
                vm.CurrentUserRole = myMembership.CommunityRole ?? "Member";
            }
            else if (currentUserId != 0 && c.CreateByUserId == currentUserId)
            {
                // Community creator without an explicit membership row = Admin
                vm.CurrentUserRole = "Admin";
            }
            else
            {
                vm.CurrentUserRole = "Member";
            }

            // Optional: provide a tiny "latest" subset if you need it somewhere else
            vm.LatestMembers = vm.Members
                .OrderByDescending(m => m.JoinDate)
                .Take(8)
                .ToList();

            return View("~/Views/Community/CommunityAdminDashboard.cshtml", vm);
        }


        [HttpGet]
        public async Task<IActionResult> GetCommunityAdminData(int communityId)
        {
            // Optional: verify current user is an Admin/Moderator of this community before exposing details
            var community = await _context.Communities
                .Include(c => c.Creator)
                .Include(c => c.Memberships)
                    .ThenInclude(m => m.User)
                .Include(c => c.BlockedUsers)
                .Include(c => c.Announcements)
                    .ThenInclude(a => a.Poster)
                .FirstOrDefaultAsync(c => c.CommunityId == communityId);

            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            var vm = new CommunityAdminDashboardViewModel
            {
                CommunityId = community.CommunityId,
                CommunityName = community.CommunityName,
                Description = community.Description,
                CommunityLocation = community.CommunityLocation,
                CommunityType = community.CommunityType,
                Status = community.Status,
                CreatedDate = community.CreatedDate,
                CommunityPic = community.CommunityPic,
                CreatedByUserId = community.CreateByUserId,
                CreatedByUserName = community.Creator?.Username ?? $"User #{community.CreateByUserId}",

                MemberCountActive = community.Memberships.Count(m => m.Status == "Active"),
                MemberCountTotal = community.Memberships.Count(),
                AdminCount = community.Memberships.Count(m => m.CommunityRole == "Admin" && m.Status == "Active"),
                ModeratorCount = community.Memberships.Count(m => m.CommunityRole == "Moderator" && m.Status == "Active"),
                BlockedUserCount = community.BlockedUsers.Count(),
                AnnouncementCount = community.Announcements.Count(),

                LatestAnnouncements = community.Announcements
                    .OrderByDescending(a => a.PostDate)
                    .Take(5)
                    .Select(a => new CommunityAdminDashboardViewModel.AnnouncementItem
                    {
                        Id = a.AnnouncementId,
                        Title = a.Title,
                        PostDate = a.PostDate,
                        PosterUserId = a.PosterUserId,
                        PosterName = a.Poster?.Username ?? $"User #{a.PosterUserId}"
                    })
                    .ToList(),

                LatestMembers = community.Memberships
                    .OrderByDescending(m => m.JoinDate)
                    .Take(8)
                    .Select(m => new CommunityAdminDashboardViewModel.MemberItem
                    {
                        UserId = m.UserId,
                        UserName = m.User?.Username ?? $"User #{m.UserId}",
                        Role = m.CommunityRole,
                        JoinDate = m.JoinDate
                    })
                    .ToList()
            };

            return Ok(new { success = true, data = vm });
        }
    }
}