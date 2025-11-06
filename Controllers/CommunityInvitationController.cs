using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    // All routes here start with /Communities/.
    [Route("Communities")]
    public class CommunityInvitationController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CommunityInvitationController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Communities/InviteSuggestions?communityId=123
        [HttpGet("InviteSuggestions")]
        public async Task<IActionResult> InviteSuggestions(int communityId)
        {
            // Active member IDs in this community
            var activeMemberIds = await _context.CommunityMembers
                .Where(m => m.CommunityId == communityId && m.Status == "Active")
                .Select(m => m.UserId)
                .ToListAsync();

            // (Optional hardening) also exclude users who already have a pending invite for this community
            var pendingInviteeIds = await _context.CommunityInvitations
                .Where(i => i.CommunityId == communityId && i.Status == "Pending")
                .Select(i => i.InviteeUserId)
                .ToListAsync();

            // 5 random users NOT in this community and not already invited
            var suggestions = await _context.Users
                .Where(u => !activeMemberIds.Contains(u.UserId) && !pendingInviteeIds.Contains(u.UserId))
                .OrderBy(u => EF.Functions.Random()) // MySQL RAND()
                .Select(u => new
                {
                    userId = u.UserId,
                    username = u.Username,
                    email = u.Email
                })
                .Take(5)
                .ToListAsync();

            return Ok(new { success = true, data = suggestions });
        }

        // POST: /Communities/InviteMember (manual: username/email OR targetUserId)
        [HttpPost("InviteMember")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteMember(
            [FromForm] int communityId,
            [FromForm] string? usernameOrEmail,
            [FromForm] string? role,
            [FromForm] string? note,
            [FromForm] int? targetUserId)
        {
            // Auth check (requires session)
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
                return Unauthorized(new { success = false, message = "Please log in." });

            var currentUserId = currentUserIdInt.Value;

            // Community exists?
            var community = await _context.Communities
                .FirstOrDefaultAsync(c => c.CommunityId == communityId && c.Status == "Active");
            if (community == null)
                return NotFound(new { success = false, message = "Community not found or inactive." });

            // Must be Admin/Moderator of this community
            var myRole = await _context.CommunityMembers
                .Where(m => m.CommunityId == communityId && m.UserId == currentUserId && m.Status == "Active")
                .Select(m => m.CommunityRole)
                .FirstOrDefaultAsync();

            if (myRole != "Admin" && myRole != "Moderator")
                return Forbid();

            // Resolve target user by id or username/email
            var roleToGive = string.IsNullOrWhiteSpace(role) ? "Member" : role.Trim();
            User? targetUser = null;

            if (targetUserId.HasValue)
            {
                targetUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == targetUserId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(usernameOrEmail))
            {
                var ident = usernameOrEmail.Trim();
                targetUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == ident || u.Email == ident);
            }

            if (targetUser == null)
                return NotFound(new { success = false, message = "Target user not found." });

            // If already Active member, block
            var existingActive = await _context.CommunityMembers
                .AnyAsync(m => m.CommunityId == communityId && m.UserId == targetUser.UserId && m.Status == "Active");
            if (existingActive)
                return Conflict(new { success = false, message = "User is already an active member." });

            // If there is a pending invite for this user/community, block duplicate
            var hasPendingInvite = await _context.CommunityInvitations
                .AnyAsync(i => i.CommunityId == communityId && i.InviteeUserId == targetUser.UserId && i.Status == "Pending");
            if (hasPendingInvite)
                return Conflict(new { success = false, message = "An invitation is already pending for this user." });

            // Create invitation (string status + DateTime.Now) — invite even if they have an Inactive membership
            var invitation = new CommunityInvitation
            {
                CommunityId   = communityId,
                InviteeUserId = targetUser.UserId,
                InviterUserId = currentUserId,
                Role          = roleToGive,
                Status        = "Pending",
                DateSent      = DateTime.Now
            };

            _context.CommunityInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Invitation sent to {targetUser.Username} for '{community.CommunityName}'."
            });
        }

        // POST: /Communities/InviteMemberById (quick invite from suggestions)
        [HttpPost("InviteMemberById")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteMemberById(
            [FromForm] int communityId,
            [FromForm] int inviteeId,
            [FromForm] string role = "Member")
        {
            // Session user
            var inviterId = HttpContext.Session.GetInt32("UserId");
            if (!inviterId.HasValue || inviterId.Value <= 0)
                return Unauthorized(new { success = false, message = "Please log in." });

            // Community active?
            var community = await _context.Communities
                .FirstOrDefaultAsync(c => c.CommunityId == communityId && c.Status == "Active");
            if (community == null)
                return NotFound(new { success = false, message = "Community not found or inactive." });

            // Already active member?
            var existingActive = await _context.CommunityMembers
                .AnyAsync(m => m.CommunityId == communityId && m.UserId == inviteeId && m.Status == "Active");
            if (existingActive)
                return Conflict(new { success = false, message = "User is already an active member." });

            // Already pending invite?
            var exists = await _context.CommunityInvitations
                .AnyAsync(i => i.CommunityId == communityId && i.InviteeUserId == inviteeId && i.Status == "Pending");
            if (exists)
                return Conflict(new { success = false, message = "This user already has a pending invitation." });

            var invite = new CommunityInvitation
            {
                CommunityId   = communityId,
                InviteeUserId = inviteeId,
                InviterUserId = inviterId.Value,
                Role          = string.IsNullOrWhiteSpace(role) ? "Member" : role,
                Status        = "Pending",
                DateSent      = DateTime.Now
            };

            _context.CommunityInvitations.Add(invite);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Invitation sent successfully." });
        }

        // POST: /Communities/RespondToInvitation  (JSON only, no redirect)
        [HttpPost("RespondToInvitation")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondToInvitation(
            [FromForm] int invitationId,
            [FromForm] string actionType)
        {
            var invite = await _context.CommunityInvitations
                .FirstOrDefaultAsync(i => i.InvitationId == invitationId);

            if (invite == null)
                return Json(new { success = false, message = "Invitation not found." });

            var isAccept  = actionType.Equals("Accept",  StringComparison.OrdinalIgnoreCase);
            var isDecline = actionType.Equals("Decline", StringComparison.OrdinalIgnoreCase);
            if (!isAccept && !isDecline)
                return Json(new { success = false, message = "Invalid action." });

            var targetStatus = isAccept ? "Accepted" : "Declined";

            // Avoid UNIQUE (community_id, invitee_user_id, status) collision by removing other
            // invites that already have the same target status for this (community, invitee).
            var duplicates = await _context.CommunityInvitations
                .Where(i => i.CommunityId == invite.CommunityId
                         && i.InviteeUserId == invite.InviteeUserId
                         && i.InvitationId != invite.InvitationId
                         && i.Status == targetStatus)
                .ToListAsync();
            if (duplicates.Count > 0)
            {
                _context.CommunityInvitations.RemoveRange(duplicates);
            }

            invite.Status        = targetStatus;
            invite.DateResponded = DateTime.Now;

            if (isDecline)
            {
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Invitation declined." });
            }

            // ACCEPT: upsert/reactivate membership inside a transaction
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var membership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(m => m.CommunityId == invite.CommunityId &&
                                              m.UserId == invite.InviteeUserId);

                if (membership == null)
                {
                    // New membership
                    membership = new CommunityMember
                    {
                        CommunityId   = invite.CommunityId,
                        UserId        = invite.InviteeUserId,
                        CommunityRole = string.IsNullOrWhiteSpace(invite.Role) ? "Member" : invite.Role,
                        Status        = "Active",
                        JoinDate      = DateTime.Now
                    };
                    _context.CommunityMembers.Add(membership);
                }
                else
                {
                    // Reactivate + set/refresh role from invitation
                    membership.Status        = "Active";
                    membership.CommunityRole = string.IsNullOrWhiteSpace(invite.Role)
                        ? (membership.CommunityRole ?? "Member")
                        : invite.Role;
                    membership.JoinDate      = DateTime.Now;

                    _context.CommunityMembers.Update(membership);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new { success = true, message = "Invitation accepted successfully!" });
            }
            catch
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Failed to accept invitation. Please try again." });
            }
        }
    }
}
