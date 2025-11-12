using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Models; // For CommunityRequest, CommunityMember, Community
using PicklePlay.Data;
using PicklePlay.ViewModels;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Collections.Generic;
using System;
using static PicklePlay.ViewModels.CommunityAdminDashboardViewModel;

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

            // --- 3. Fetch Pending AND Rejected Requests ---
            var pendingRequests = new List<object>();
            var rejectedRequests = new List<object>();

            if (currentUserId > 0)
            {
                // Pending requests
                pendingRequests = await _context.CommunityMembers
                    .Include(cm => cm.Community)
                    .Where(cm => cm.Status == "Pending" && cm.UserId == currentUserId)
                    .Select(cm => new
                    {
                        requestId = cm.MemberId,
                        name = cm.Community.CommunityName,
                        location = cm.Community.CommunityLocation,
                        type = cm.Community.CommunityType,
                        requestDate = cm.JoinDate,
                        status = cm.Status
                    })
                    .ToListAsync<object>();

                // Rejected requests (for notification purposes)
                rejectedRequests = await _context.CommunityMembers
                    .Include(cm => cm.Community)
                    .Where(cm => cm.Status == "Rejected" && cm.UserId == currentUserId)
                    .Select(cm => new
                    {
                        requestId = cm.MemberId,
                        communityId = cm.CommunityId,
                        name = cm.Community.CommunityName,
                        location = cm.Community.CommunityLocation,
                        type = cm.Community.CommunityType,
                        requestDate = cm.JoinDate,
                        status = cm.Status
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

            // --- 5. Fetch and Select Suggested Communities (Max 9, excluding joined/pending/rejected) ---
            var excludedCommunityIds = await _context.CommunityMembers
                .Where(cm => cm.UserId == currentUserId &&
                            (cm.Status == "Active" || cm.Status == "Pending" || cm.Status == "Rejected"))
                .Select(cm => cm.CommunityId)
                .ToListAsync();

            var suggestedCommunitiesQuery = _context.Communities
                .Where(c => !excludedCommunityIds.Contains(c.CommunityId) && c.Status == "Active")
                .Select(c => new
                {
                    id = c.CommunityId,
                    name = c.CommunityName,
                    description = c.Description,
                    location = c.CommunityLocation,
                    type = c.CommunityType,
                    userRole = (string)null!,
                    memberCount = communityStats.ContainsKey(c.CommunityId) ? communityStats[c.CommunityId] : 0,
                    gameCount = 0,
                    icon = "compass"
                });

            var suggestedCommunities = await suggestedCommunitiesQuery
                .OrderBy(c => Guid.NewGuid())
                .Take(9)
                .ToListAsync<object>();

            return Ok(new
            {
                pendingRequests = pendingRequests,
                rejectedRequests = rejectedRequests, // Add rejected requests to response
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
            var community = await _context.Communities.FirstOrDefaultAsync(c => c.CommunityId == communityId);
            if (community == null)
            {
                return NotFound(new { success = false, message = "Community not found." });
            }

            // 3. Check if user is blocked from this community
            var isBlocked = await _context.CommunityBlockLists
                .AnyAsync(b => b.CommunityId == communityId && b.UserId == userId);

            if (isBlocked)
            {
                return BadRequest(new { success = false, message = "You are blocked from joining this community." });
            }

            // 4. Check for existing membership (Active, Inactive, or Pending)
            var existingMembership = await _context.CommunityMembers
                .FirstOrDefaultAsync(cm => cm.CommunityId == communityId && cm.UserId == userId);

            if (existingMembership != null)
            {
                if (existingMembership.Status == "Active")
                {
                    return BadRequest(new { success = false, message = "You are already an active member of this community." });
                }
                else if (existingMembership.Status == "Pending")
                {
                    return BadRequest(new { success = false, message = "You already have a pending request to join this community." });
                }

                // REACTIVATE Inactive Membership - but for private communities, this should be a new request
                if (community.CommunityType == "Private")
                {
                    // For private communities, changing from Inactive to Active requires admin approval
                    existingMembership.Status = "Pending";
                    existingMembership.JoinDate = DateTime.UtcNow;
                    _context.CommunityMembers.Update(existingMembership);
                }
                else
                {
                    // For public communities, allow immediate reactivation
                    existingMembership.Status = "Active";
                    existingMembership.JoinDate = DateTime.UtcNow;
                    _context.CommunityMembers.Update(existingMembership);
                }
            }
            else
            {
                // 5. Create NEW Membership
                var membership = new CommunityMember
                {
                    CommunityId = communityId,
                    UserId = userId,
                    CommunityRole = "Member",
                    Status = community.CommunityType == "Private" ? "Pending" : "Active", // Set status based on community type
                    JoinDate = DateTime.UtcNow
                };
                _context.CommunityMembers.Add(membership);
            }

            try
            {
                await _context.SaveChangesAsync();

                string message = community.CommunityType == "Private"
                    ? "Request to join community submitted! Waiting for admin approval."
                    : "Successfully joined the community!";

                return Ok(new
                {
                    success = true,
                    message = message,
                    communityId = communityId,
                    isPrivateCommunity = community.CommunityType == "Private",
                    requiresApproval = community.CommunityType == "Private"
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

        // POST: /Communities/AcceptJoinRequest
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptJoinRequest(int requestId)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            try
            {
                // Find the pending membership request
                var membershipRequest = await _context.CommunityMembers
                    .Include(m => m.Community)
                    .FirstOrDefaultAsync(m => m.MemberId == requestId && m.Status == "Pending");

                if (membershipRequest == null)
                {
                    return NotFound(new { success = false, message = "Join request not found." });
                }

                // Verify the current user has admin privileges for this community
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == membershipRequest.CommunityId &&
                                             cm.UserId == currentUserId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                if (currentUserMembership == null)
                {
                    return Forbid("You don't have permission to accept join requests in this community.");
                }

                // Update the membership status to Active
                membershipRequest.Status = "Active";
                _context.CommunityMembers.Update(membershipRequest);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Join request accepted successfully!",
                    requestId = requestId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error accepting join request: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while accepting the join request."
                });
            }
        }

        // POST: /Communities/RejectJoinRequest
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectJoinRequest(int requestId)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            try
            {
                // Find the pending membership request with user and community info
                var membershipRequest = await _context.CommunityMembers
                    .Include(m => m.Community)
                    .Include(m => m.User)
                    .FirstOrDefaultAsync(m => m.MemberId == requestId && m.Status == "Pending");

                if (membershipRequest == null)
                {
                    return NotFound(new { success = false, message = "Join request not found." });
                }

                // Verify the current user has admin privileges for this community
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == membershipRequest.CommunityId &&
                                             cm.UserId == currentUserId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                if (currentUserMembership == null)
                {
                    return Forbid("You don't have permission to reject join requests in this community.");
                }

                // Store user info for notification before updating
                var rejectedUserId = membershipRequest.UserId;
                var rejectedUserName = membershipRequest.User?.Username ?? "User";
                var communityName = membershipRequest.Community?.CommunityName ?? "Community";

                // Update status to Rejected instead of removing
                membershipRequest.Status = "Rejected";
                _context.CommunityMembers.Update(membershipRequest);

                // Send notification to the rejected user
                var notification = new Notification
                {
                    UserId = rejectedUserId,
                    Message = $"Your request to join '{communityName}' has been rejected by the community admin.",
                    LinkUrl = Url.Action("Community", "Home"),
                    IsRead = false,
                    DateCreated = DateTime.UtcNow
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Join request rejected successfully!",
                    requestId = requestId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error rejecting join request: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while rejecting the join request."
                });
            }
        }

        // POST: /Communities/RemoveRejectedRequest
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveRejectedRequest(int requestId)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            try
            {
                // Find the rejected membership request
                var rejectedRequest = await _context.CommunityMembers
                    .FirstOrDefaultAsync(m => m.MemberId == requestId && m.Status == "Rejected" && m.UserId == currentUserId);

                if (rejectedRequest == null)
                {
                    return NotFound(new { success = false, message = "Rejected request not found." });
                }

                // Remove the rejected request
                _context.CommunityMembers.Remove(rejectedRequest);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Rejected request removed successfully!",
                    requestId = requestId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error removing rejected request: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while removing the rejected request."
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

        // GET: /Communities/CommunityAdminDashboard
        [HttpGet]
        public async Task<IActionResult> CommunityAdminDashboard(int communityId)
        {
            var c = await _context.Communities
                .Include(x => x.Creator)
                .Include(x => x.Memberships).ThenInclude(m => m.User)
                .Include(x => x.BlockedUsers).ThenInclude(b => b.BlockedUser)
                .Include(x => x.BlockedUsers).ThenInclude(b => b.BlockingAdmin)
                .Include(x => x.Announcements).ThenInclude(a => a.Poster)
                .FirstOrDefaultAsync(x => x.CommunityId == communityId);

            if (c == null) return NotFound();

            // ---- Resolve current user id robustly (Session -> Claims -> Username lookup) ----
            int currentUserId = HttpContext.Session.GetInt32("UserId") ?? 0;

            if (currentUserId == 0)
            {
                var claimId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(claimId, out var fromClaim))
                    currentUserId = fromClaim;
            }
            if (currentUserId == 0)
            {
                var uname = HttpContext.Session.GetString("Username") ?? User?.Identity?.Name;
                if (!string.IsNullOrWhiteSpace(uname))
                {
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
                CurrentUserId = currentUserId,

                MemberCountActive = c.Memberships.Count(m => m.Status == "Active"),
                MemberCountTotal = c.Memberships.Count(),
                AdminCount = c.Memberships.Count(m => m.Status == "Active" && m.CommunityRole == "Admin"),
                ModeratorCount = c.Memberships.Count(m => m.Status == "Active" && m.CommunityRole == "Moderator"),
                BlockedUserCount = c.BlockedUsers?.Where(b => b.Status == "Active").Count() ?? 0,
                AnnouncementCount = c.Announcements?.Count ?? 0,

                LatestAnnouncements = c.Announcements?
                    .OrderByDescending(a => a.PostDate)
                    .Take(5)
                    .Select(a => new CommunityAdminDashboardViewModel.AnnouncementItem
                    {
                        Id = a.AnnouncementId,
                        Title = a.Title,
                        Content = a.Content,
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
                    }).ToList(),

                // *** ADDED: Populate BlockedUsers ***
                BlockedUsers = c.BlockedUsers?
                    .Where(b => b.Status == "Active")
                    .OrderByDescending(b => b.BlockDate)
                    .Select(b => new CommunityAdminDashboardViewModel.BlockedUserItem
                    {
                        BlockId = b.BlockId,
                        UserId = b.UserId,
                        UserName = b.BlockedUser?.Username ?? $"User #{b.UserId}",
                        BlockedByUserName = b.BlockingAdmin?.Username ?? $"User #{b.BlockByAdminId}",
                        BlockReason = b.BlockReason,
                        BlockDate = b.BlockDate
                    }).ToList() ?? new List<CommunityAdminDashboardViewModel.BlockedUserItem>()
            };

            // ---- Correctly determine viewer's role ----
            var myMembership = c.Memberships.FirstOrDefault(m => m.UserId == currentUserId);
            if (myMembership != null)
            {
                vm.CurrentUserRole = myMembership.CommunityRole ?? "Member";
            }
            else if (currentUserId != 0 && c.CreateByUserId == currentUserId)
            {
                vm.CurrentUserRole = "Admin";
            }
            else
            {
                vm.CurrentUserRole = "Member";
            }

            vm.LatestMembers = vm.Members
                .OrderByDescending(m => m.JoinDate)
                .Take(8)
                .ToList();

            return View("~/Views/Community/CommunityAdminDashboard.cshtml", vm);
        }

        // GET: /Communities/GetCommunityAdminData
        [HttpGet]
        public async Task<IActionResult> GetCommunityAdminData(int communityId)
        {

            int currentUserId = HttpContext.Session.GetInt32("UserId") ?? 0;

            if (currentUserId == 0)
            {
                var claimId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(claimId, out var fromClaim))
                    currentUserId = fromClaim;
            }
            if (currentUserId == 0)
            {
                var uname = HttpContext.Session.GetString("Username") ?? User?.Identity?.Name;
                if (!string.IsNullOrWhiteSpace(uname))
                {
                    currentUserId = await _context.Users
                        .Where(u => u.Username == uname)
                        .Select(u => u.UserId)
                        .FirstOrDefaultAsync();
                }
            }
            var community = await _context.Communities
                .Include(c => c.Creator)
                .Include(c => c.Memberships)
                    .ThenInclude(m => m.User)
                .Include(c => c.BlockedUsers)
                    .ThenInclude(b => b.BlockedUser) // Include blocked user details
                .Include(c => c.BlockedUsers)
                    .ThenInclude(b => b.BlockingAdmin) // Include admin who blocked
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
                CurrentUserId = currentUserId,

                MemberCountActive = community.Memberships.Count(m => m.Status == "Active"),
                MemberCountTotal = community.Memberships.Count(),
                AdminCount = community.Memberships.Count(m => m.CommunityRole == "Admin" && m.Status == "Active"),
                ModeratorCount = community.Memberships.Count(m => m.CommunityRole == "Moderator" && m.Status == "Active"),
                BlockedUserCount = community.BlockedUsers.Count(b => b.Status == "Active"), // Only count active blocks
                AnnouncementCount = community.Announcements.Count(),

                LatestAnnouncements = community.Announcements
                    .OrderByDescending(a => a.PostDate)
                    .Take(5)
                    .Select(a => new CommunityAdminDashboardViewModel.AnnouncementItem
                    {
                        Id = a.AnnouncementId,
                        Title = a.Title,
                        Content = a.Content,
                        PostDate = a.PostDate,
                        PosterUserId = a.PosterUserId,
                        PosterName = a.Poster?.Username ?? $"User #{a.PosterUserId}"
                    })
                    .ToList(),

                // --- AMENDED: Include the full Members list for client-side table refresh ---
                Members = community.Memberships
                    .OrderByDescending(m => m.JoinDate)
                    .Select(m => new CommunityAdminDashboardViewModel.MemberItem
                    {
                        UserId = m.UserId,
                        UserName = m.User?.Username ?? $"User #{m.UserId}",
                        Role = m.CommunityRole,
                        Status = m.Status,
                        JoinDate = m.JoinDate
                    })
                    .ToList(),
                // --------------------------------------------------------------------------

                LatestMembers = community.Memberships
                    .OrderByDescending(m => m.JoinDate)
                    .Take(8)
                    .Select(m => new CommunityAdminDashboardViewModel.MemberItem
                    {
                        UserId = m.UserId,
                        UserName = m.User?.Username ?? $"User #{m.UserId}",
                        Role = m.CommunityRole,
                        Status = m.Status, // Added status for consistency
                        JoinDate = m.JoinDate
                    })
                    .ToList(),

                JoinRequests = community.Memberships
                    .Where(m => m.Status == "Pending")
                    .OrderByDescending(m => m.JoinDate)
                    .Select(m => new CommunityAdminDashboardViewModel.JoinRequestItem
                    {
                        RequestId = m.MemberId,
                        UserId = m.UserId,
                        Username = m.User?.Username ?? $"User #{m.UserId}",
                        RequestedDate = m.JoinDate
                    })
                    .ToList(),

                // --- ADDED: Blocked Users List ---
                BlockedUsers = community.BlockedUsers
                    .Where(b => b.Status == "Active") // Only show active blocks
                    .OrderByDescending(b => b.BlockDate)
                    .Select(b => new CommunityAdminDashboardViewModel.BlockedUserItem
                    {
                        BlockId = b.BlockId,
                        UserId = b.UserId,
                        UserName = b.BlockedUser?.Username ?? $"User #{b.UserId}",
                        BlockedByUserName = b.BlockingAdmin?.Username ?? $"User #{b.BlockByAdminId}",
                        BlockReason = b.BlockReason,
                        BlockDate = b.BlockDate
                    })
                    .ToList()

            };

            return Ok(new { success = true, data = vm });
        }

        // POST: /Communities/CreateAnnouncement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAnnouncement([FromForm] CommunityAdminDashboardViewModel.CreateAnnouncementViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data submitted.", errors = ModelState.Values.SelectMany(v => v.Errors) });
            }

            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == model.CommunityId);

                if (community == null)
                {
                    return NotFound(new { success = false, message = "Community not found." });
                }

                var userMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == model.CommunityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             (cm.CommunityRole == "Admin" || cm.CommunityRole == "Moderator"));

                var isCreator = community.CreateByUserId == userId;

                if (userMembership == null && !isCreator)
                {
                    return Forbid("You don't have permission to create announcements in this community.");
                }

                // Set Malaysia timezone (UTC+8)
                var malaysiaTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
                var malaysiaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, malaysiaTime);

                var announcement = new CommunityAnnouncement
                {
                    CommunityId = model.CommunityId,
                    PosterUserId = userId,
                    Title = model.Title.Trim(),
                    Content = model.Content.Trim(),
                    PostDate = malaysiaNow,
                    ExpiryDate = model.ExpiryDate?.ToUniversalTime() // Store expiry date in UTC
                };

                _context.CommunityAnnouncements.Add(announcement);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Announcement created successfully!",
                    announcementId = announcement.AnnouncementId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error creating announcement: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while creating the announcement."
                });
            }
        }

        // POST: /Communities/AssignRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest request)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            // Validate role
            if (string.IsNullOrWhiteSpace(request.NewRole) || (request.NewRole != "Admin" && request.NewRole != "Member"))
            {
                return BadRequest(new { success = false, message = "Invalid role specified. Must be Admin or Member." });
            }

            try
            {
                // Verify the current user has admin privileges
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == request.CommunityId &&
                                             cm.UserId == currentUserId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                if (currentUserMembership == null)
                {
                    return Forbid("You don't have permission to assign roles in this community.");
                }

                // Find the target membership
                var targetMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == request.CommunityId &&
                                             cm.UserId == request.UserId &&
                                             cm.Status == "Active");

                if (targetMembership == null)
                {
                    return NotFound(new { success = false, message = "Member not found or inactive." });
                }

                // Prevent role changes on community creator
                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == request.CommunityId);

                if (community != null && targetMembership.UserId == community.CreateByUserId)
                {
                    return BadRequest(new { success = false, message = "Cannot change the role of the community creator." });
                }

                // PREVENT SELF-DEMOTION: Admin cannot demote themselves to Member
                if (targetMembership.UserId == currentUserId && request.NewRole == "Member")
                {
                    return BadRequest(new { success = false, message = "You cannot demote yourself from Admin to Member. Ask another admin to change your role." });
                }

                // PREVENT SELF-DEMOTION: Admin cannot demote themselves to Member
                if (targetMembership.UserId == currentUserId && request.NewRole == "Member")
                {
                    return BadRequest(new { success = false, message = "You cannot demote yourself from Admin to Member. Ask another admin to change your role." });
                }

                // Update the role
                string oldRole = targetMembership.CommunityRole;
                targetMembership.CommunityRole = request.NewRole;
                _context.CommunityMembers.Update(targetMembership);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Member role has been successfully changed from {oldRole} to {request.NewRole}.",
                    userId = request.UserId,
                    newRole = request.NewRole
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error assigning role: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while updating the member role."
                });
            }
        }




        // POST: /Communities/KickMember
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KickMember([FromBody] KickMemberRequest request)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            try
            {
                // Verify the current user has admin privileges
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == request.CommunityId &&
                                             cm.UserId == currentUserId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                if (currentUserMembership == null)
                {
                    return Forbid("You don't have permission to kick members from this community.");
                }

                // Find the target membership
                var targetMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == request.CommunityId &&
                                             cm.UserId == request.UserId &&
                                             cm.Status == "Active");

                if (targetMembership == null)
                {
                    return NotFound(new { success = false, message = "Member not found or already inactive." });
                }

                // Prevent kicking the community creator
                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == request.CommunityId);

                if (community != null && targetMembership.UserId == community.CreateByUserId)
                {
                    return BadRequest(new { success = false, message = "Cannot kick the community creator." });
                }

                // ALLOW self-kicking (admin can remove themselves)
                // Update status to Inactive (soft delete)
                targetMembership.Status = "Inactive";
                _context.CommunityMembers.Update(targetMembership);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Member has been successfully removed from the community.",
                    userId = request.UserId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error kicking member: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while removing the member."
                });
            }
        }



        // POST: /Communities/BlockMember
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BlockMember(int communityId, int userId, string? blockReason = null)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            try
            {
                // Verify the current user has admin privileges
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == communityId &&
                                             cm.UserId == currentUserId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                if (currentUserMembership == null)
                {
                    return Forbid("You don't have permission to block members in this community.");
                }

                // Find the target membership
                var targetMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == communityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active");

                if (targetMembership == null)
                {
                    return NotFound(new { success = false, message = "Member not found or already inactive." });
                }

                // Prevent blocking yourself
                if (targetMembership.UserId == currentUserId)
                {
                    return BadRequest(new { success = false, message = "You cannot block yourself." });
                }

                // Prevent blocking the community creator
                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == communityId);

                if (community != null && targetMembership.UserId == community.CreateByUserId)
                {
                    return BadRequest(new { success = false, message = "Cannot block the community creator." });
                }

                // Check if user is already blocked (with Active status)
                var existingBlock = await _context.CommunityBlockLists
                    .FirstOrDefaultAsync(b => b.CommunityId == communityId &&
                                            b.UserId == userId &&
                                            b.Status == "Active");

                if (existingBlock != null)
                {
                    return BadRequest(new { success = false, message = "This user is already blocked." });
                }

                // If there's an existing inactive block, reactivate it
                var inactiveBlock = await _context.CommunityBlockLists
                    .FirstOrDefaultAsync(b => b.CommunityId == communityId &&
                                            b.UserId == userId &&
                                            b.Status == "Inactive");

                if (inactiveBlock != null)
                {
                    inactiveBlock.Status = "Active";
                    inactiveBlock.BlockByAdminId = currentUserId;
                    inactiveBlock.BlockReason = blockReason ?? "Blocked by admin";
                    inactiveBlock.BlockDate = DateTime.UtcNow;
                    _context.CommunityBlockLists.Update(inactiveBlock);
                }
                else
                {
                    // Add to blocked users table
                    var blockedUser = new CommunityBlockList
                    {
                        CommunityId = communityId,
                        UserId = userId,
                        BlockByAdminId = currentUserId,
                        BlockReason = blockReason ?? "Blocked by admin",
                        BlockDate = DateTime.UtcNow,
                        Status = "Active" // Set status to Active
                    };
                    _context.CommunityBlockLists.Add(blockedUser);
                }

                // Also set membership to inactive
                targetMembership.Status = "Inactive";
                _context.CommunityMembers.Update(targetMembership);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Member has been successfully blocked and removed from the community.",
                    userId = userId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error blocking member: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while blocking the member."
                });
            }
        }
        // POST: /Communities/UnblockMember
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnblockMember(int communityId, int userId)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int currentUserId = currentUserIdInt.Value;

            try
            {
                // Verify the current user has admin privileges
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == communityId &&
                                             cm.UserId == currentUserId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                if (currentUserMembership == null)
                {
                    return Forbid("You don't have permission to unblock members in this community.");
                }

                // Find the active block record
                var blockRecord = await _context.CommunityBlockLists
                    .FirstOrDefaultAsync(b => b.CommunityId == communityId &&
                                            b.UserId == userId &&
                                            b.Status == "Active");

                if (blockRecord == null)
                {
                    return NotFound(new { success = false, message = "User is not currently blocked in this community." });
                }

                // Soft delete by setting status to Inactive instead of removing
                blockRecord.Status = "Inactive";
                _context.CommunityBlockLists.Update(blockRecord);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "User has been successfully unblocked and can now rejoin the community.",
                    userId = userId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error unblocking member: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while unblocking the user."
                });
            }
        }

        // POST: /Communities/EditAnnouncement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAnnouncement([FromForm] CommunityAdminDashboardViewModel.EditAnnouncementViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data submitted." });
            }

            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var announcement = await _context.CommunityAnnouncements
                    .FirstOrDefaultAsync(a => a.AnnouncementId == model.AnnouncementId && a.CommunityId == model.CommunityId);

                if (announcement == null)
                {
                    return NotFound(new { success = false, message = "Announcement not found." });
                }

                // Verify user has permission to edit this announcement
                var userMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == model.CommunityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             (cm.CommunityRole == "Admin" || cm.CommunityRole == "Moderator"));

                var isCreator = announcement.PosterUserId == userId;
                var isCommunityCreator = await _context.Communities
                    .AnyAsync(c => c.CommunityId == model.CommunityId && c.CreateByUserId == userId);

                if (userMembership == null && !isCreator && !isCommunityCreator)
                {
                    return Forbid("You don't have permission to edit this announcement.");
                }

                // Update announcement
                announcement.Title = model.Title.Trim();
                announcement.Content = model.Content.Trim();
                announcement.ExpiryDate = model.ExpiryDate?.ToUniversalTime();

                _context.CommunityAnnouncements.Update(announcement);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Announcement updated successfully!",
                    announcementId = announcement.AnnouncementId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error editing announcement: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while updating the announcement."
                });
            }
        }

        // POST: /Communities/DeleteAnnouncement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAnnouncement([FromForm] CommunityAdminDashboardViewModel.DeleteAnnouncementViewModel model)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var announcement = await _context.CommunityAnnouncements
                    .FirstOrDefaultAsync(a => a.AnnouncementId == model.AnnouncementId && a.CommunityId == model.CommunityId);

                if (announcement == null)
                {
                    return NotFound(new { success = false, message = "Announcement not found." });
                }

                // Verify user has permission to delete this announcement
                var userMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == model.CommunityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             (cm.CommunityRole == "Admin" || cm.CommunityRole == "Moderator"));

                var isCreator = announcement.PosterUserId == userId;
                var isCommunityCreator = await _context.Communities
                    .AnyAsync(c => c.CommunityId == model.CommunityId && c.CreateByUserId == userId);

                if (userMembership == null && !isCreator && !isCommunityCreator)
                {
                    return Forbid("You don't have permission to delete this announcement.");
                }

                _context.CommunityAnnouncements.Remove(announcement);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Announcement deleted successfully!"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error deleting announcement: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while deleting the announcement."
                });
            }
        }

        // POST: /Communities/ToggleAnnouncementVisibility
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAnnouncementVisibility(int announcementId, int communityId)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var announcement = await _context.CommunityAnnouncements
                    .FirstOrDefaultAsync(a => a.AnnouncementId == announcementId && a.CommunityId == communityId);

                if (announcement == null)
                {
                    return NotFound(new { success = false, message = "Announcement not found." });
                }

                // Verify user has permission to modify this announcement
                var userMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == communityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             (cm.CommunityRole == "Admin" || cm.CommunityRole == "Moderator"));

                var isCreator = announcement.PosterUserId == userId;
                var isCommunityCreator = await _context.Communities
                    .AnyAsync(c => c.CommunityId == communityId && c.CreateByUserId == userId);

                if (userMembership == null && !isCreator && !isCommunityCreator)
                {
                    return Forbid("You don't have permission to modify this announcement.");
                }

                // For simplicity, we'll use ExpiryDate to control visibility
                // If ExpiryDate is in past, announcement is "hidden"
                if (announcement.ExpiryDate.HasValue && announcement.ExpiryDate < DateTime.UtcNow)
                {
                    // Make visible by clearing expiry date
                    announcement.ExpiryDate = null;
                }
                else
                {
                    // Hide by setting expiry to past
                    announcement.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
                }

                _context.CommunityAnnouncements.Update(announcement);
                await _context.SaveChangesAsync();

                var isNowVisible = announcement.ExpiryDate == null || announcement.ExpiryDate > DateTime.UtcNow;

                return Ok(new
                {
                    success = true,
                    message = isNowVisible ? "Announcement is now visible!" : "Announcement is now hidden!",
                    isVisible = isNowVisible
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error toggling announcement visibility: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while updating the announcement."
                });
            }
        }

        // POST: /Communities/UpdateProfileImage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfileImage([FromForm] ProfileImageViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data submitted." });
            }

            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                // Verify the current user has admin privileges
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == model.CommunityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                var isCreator = await _context.Communities
                    .AnyAsync(c => c.CommunityId == model.CommunityId && c.CreateByUserId == userId);

                if (currentUserMembership == null && !isCreator)
                {
                    return Forbid("You don't have permission to update the community profile image.");
                }

                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == model.CommunityId);

                if (community == null)
                {
                    return NotFound(new { success = false, message = "Community not found." });
                }

                string? imageUrl = null;

                // Handle image upload
                if (model.ProfileImage != null && model.ProfileImage.Length > 0)
                {
                    // Validate file type
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                    var fileExtension = Path.GetExtension(model.ProfileImage.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        return BadRequest(new { success = false, message = "Invalid file type. Only JPG, JPEG, PNG, GIF, and BMP are allowed." });
                    }

                    // Validate file size (max 5MB)
                    if (model.ProfileImage.Length > 5 * 1024 * 1024)
                    {
                        return BadRequest(new { success = false, message = "File size too large. Maximum size is 5MB." });
                    }

                    // Create uploads directory if it doesn't exist
                    var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "communities");
                    if (!Directory.Exists(uploadsDir))
                    {
                        Directory.CreateDirectory(uploadsDir);
                    }

                    // Generate unique filename
                    var fileName = $"community_{community.CommunityId}_{Guid.NewGuid()}{fileExtension}";
                    var filePath = Path.Combine(uploadsDir, fileName);

                    // Save the file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.ProfileImage.CopyToAsync(stream);
                    }

                    // Set the image URL (relative path)
                    imageUrl = $"/uploads/communities/{fileName}";

                    // Delete old image if exists
                    if (!string.IsNullOrEmpty(community.CommunityPic))
                    {
                        var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", community.CommunityPic.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                    }

                    community.CommunityPic = imageUrl;
                }
                else if (model.ProfileImage == null && string.IsNullOrEmpty(model.CurrentImageUrl))
                {
                    // Remove existing image if no new image and no current image URL
                    if (!string.IsNullOrEmpty(community.CommunityPic))
                    {
                        var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", community.CommunityPic.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                        community.CommunityPic = null;
                    }
                }

                _context.Communities.Update(community);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Profile image updated successfully!",
                    imageUrl = community.CommunityPic
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error updating profile image: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while updating the profile image."
                });
            }
        }

        // POST: /Communities/UpdatePrivacySettings
        // POST: /Communities/UpdatePrivacySettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePrivacySettings([FromForm] PrivacySettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data submitted." });
            }

            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                // Verify the current user has admin privileges
                var currentUserMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == model.CommunityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                var isCreator = await _context.Communities
                    .AnyAsync(c => c.CommunityId == model.CommunityId && c.CreateByUserId == userId);

                if (currentUserMembership == null && !isCreator)
                {
                    return Forbid("You don't have permission to update community privacy settings.");
                }

                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == model.CommunityId);

                if (community == null)
                {
                    return NotFound(new { success = false, message = "Community not found." });
                }

                // SERVER-SIDE VALIDATION: Prevent Private to Public change if pending requests exist
                if (community.CommunityType == "Private" && model.CommunityType == "Public")
                {
                    var pendingRequestsCount = await _context.CommunityMembers
                        .CountAsync(cm => cm.CommunityId == model.CommunityId && cm.Status == "Pending");

                    if (pendingRequestsCount > 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Cannot change to Public community while there are {pendingRequestsCount} pending join request(s). Please resolve all pending requests first."
                        });
                    }
                }

                // Update community type
                community.CommunityType = model.CommunityType;
                _context.Communities.Update(community);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Community privacy settings updated to {model.CommunityType} successfully!",
                    communityType = model.CommunityType
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error updating privacy settings: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while updating privacy settings."
                });
            }
        }

        // POST: /Communities/DeleteCommunity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCommunity([FromForm] DeleteCommunityViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data submitted." });
            }

            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var community = await _context.Communities
                    .Include(c => c.Memberships)
                    .Include(c => c.Announcements)
                    .Include(c => c.BlockedUsers)
                    .FirstOrDefaultAsync(c => c.CommunityId == model.CommunityId);

                if (community == null)
                {
                    return NotFound(new { success = false, message = "Community not found." });
                }

                // Verify user is community admin
                var userMembership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == model.CommunityId &&
                                             cm.UserId == userId &&
                                             cm.Status == "Active" &&
                                             cm.CommunityRole == "Admin");

                var isCreator = community.CreateByUserId == userId;

                if (userMembership == null && !isCreator)
                {
                    return Forbid("Only community admins can delete this community.");
                }

                // Verify community name matches for confirmation
                if (!community.CommunityName.Equals(model.ConfirmationName, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { success = false, message = "Community name does not match. Please type the exact community name to confirm deletion." });
                }

                // Check for pending join requests (for private communities)
                if (community.CommunityType == "Private")
                {
                    var pendingRequests = community.Memberships
                        .Where(m => m.Status == "Pending")
                        .Any();

                    if (pendingRequests)
                    {
                        return BadRequest(new { success = false, message = "Cannot delete private community while there are pending join requests. Please resolve all pending requests first." });
                    }
                }

                // Check for active games/competitions associated with this community
                var activeGames = await _context.Schedules
                    .Where(s => s.Location != null && s.Location.Contains(community.CommunityName) &&
                               (s.Status == ScheduleStatus.Active || s.Status == ScheduleStatus.Open || s.Status == ScheduleStatus.InProgress))
                    .AnyAsync();

                if (activeGames)
                {
                    return BadRequest(new { success = false, message = "Cannot delete community while there are active games or competitions associated with it. Please cancel or complete all active events first." });
                }

                // Check for upcoming competitions
                var upcomingCompetitions = await _context.Schedules
                    .Where(s => s.Location != null && s.Location.Contains(community.CommunityName) &&
                               s.ScheduleType == ScheduleType.Competition &&
                               s.StartTime > DateTime.UtcNow &&
                               s.Status != ScheduleStatus.Cancelled)
                    .AnyAsync();

                if (upcomingCompetitions)
                {
                    return BadRequest(new { success = false, message = "Cannot delete community while there are upcoming competitions. Please cancel all upcoming competitions first." });
                }

                // Get ALL active members to notify (INCLUDING the deletor)
                var activeMembers = community.Memberships
                    .Where(m => m.Status == "Active")
                    .Select(m => m.UserId)
                    .ToList();

                // SOFT DELETE: Update status to "Deleted" and store deletion metadata
                community.Status = "Deleted";
                community.CommunityName = $"[DELETED] {community.CommunityName}";

                // UPDATED: Format the reason field as "This community was deleted on YYYY-MM-DD + reason"
                var deletionDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
                community.DeletionReason = $"This community was deleted on {deletionDate}. Reason: {model.DeleteReason}";

                community.DeletedByUserId = userId;
                community.DeletionDate = DateTime.UtcNow;

                _context.Communities.Update(community);

                // Archive or remove related data
                _context.CommunityAnnouncements.RemoveRange(community.Announcements);
                _context.CommunityBlockLists.RemoveRange(community.BlockedUsers);

                // Update all memberships to inactive
                foreach (var membership in community.Memberships)
                {
                    membership.Status = "Inactive";
                }

                // SEND NOTIFICATIONS TO ALL FORMER MEMBERS (INCLUDING DELETOR)
                if (model.NotifyMembers && activeMembers.Any())
                {
                    var currentUser = await _context.Users.FindAsync(userId);
                    var deleterName = currentUser?.Username ?? "Admin";
                    var originalCommunityName = community.CommunityName.Replace("[DELETED] ", "");

                    foreach (var memberId in activeMembers)
                    {
                        // Create different message for deletor vs other members
                        string message;
                        if (memberId == userId)
                        {
                            // Message for the person who deleted the community
                            message = $"You have successfully deleted the community '{originalCommunityName}'. Reason: {model.DeleteReason}";
                        }
                        else
                        {
                            // Message for other members
                            message = $"The community '{originalCommunityName}' has been deleted by {deleterName}. Reason: {model.DeleteReason}";
                        }

                        var notification = new Notification
                        {
                            UserId = memberId,
                            Message = message,
                            LinkUrl = null, // Redirect to communities list
                            IsRead = false,
                            DateCreated = DateTime.UtcNow
                        };
                        _context.Notifications.Add(notification);
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Community has been successfully deleted.",
                    communityId = model.CommunityId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database error deleting community: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while deleting the community."
                });
            }
        }

        // GET: /Communities/CheckCommunityDeletionRules
        [HttpGet]
        public async Task<IActionResult> CheckCommunityDeletionRules(int communityId)
        {
            try
            {
                var community = await _context.Communities
                    .FirstOrDefaultAsync(c => c.CommunityId == communityId);

                if (community == null)
                {
                    return NotFound(new { success = false, message = "Community not found." });
                }

                // Check for pending join requests (for private communities)
                bool hasPendingRequests = false;
                if (community.CommunityType == "Private")
                {
                    hasPendingRequests = await _context.CommunityMembers
                        .AnyAsync(cm => cm.CommunityId == communityId && cm.Status == "Pending");
                }

                // Check for active games/competitions
                bool hasActiveGames = await _context.Schedules
                    .Where(s => s.Location != null && s.Location.Contains(community.CommunityName) &&
                               (s.Status == ScheduleStatus.Active || s.Status == ScheduleStatus.Open || s.Status == ScheduleStatus.InProgress))
                    .AnyAsync();

                // Check for upcoming competitions
                bool hasUpcomingCompetitions = await _context.Schedules
                    .Where(s => s.Location != null && s.Location.Contains(community.CommunityName) &&
                               s.ScheduleType == ScheduleType.Competition &&
                               s.StartTime > DateTime.UtcNow &&
                               s.Status != ScheduleStatus.Cancelled)
                    .AnyAsync();

                var rules = new
                {
                    hasPendingRequests = hasPendingRequests,
                    hasActiveGames = hasActiveGames,
                    hasUpcomingCompetitions = hasUpcomingCompetitions,
                    canDelete = !hasPendingRequests && !hasActiveGames && !hasUpcomingCompetitions,
                    communityType = community.CommunityType
                };

                return Ok(new { success = true, data = rules });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking deletion rules: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error checking deletion rules." });
            }
        }
    }
}