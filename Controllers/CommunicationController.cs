using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;

namespace PicklePlay.Controllers
{
    public class CommunicationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CommunicationController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        public async Task<IActionResult> Index()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Auth");
            }

            // 1) Pending team invitations
            var teamInvitations = await _context.TeamInvitations
                .Where(inv => inv.InviteeUserId == currentUserId.Value && inv.Status == InvitationStatus.Pending)
                .Include(inv => inv.Team)
                .Include(inv => inv.Inviter)
                .OrderByDescending(inv => inv.DateSent)
                .ToListAsync();

            // 2) Pending friend requests (incoming)
            var friendRequests = await _context.Friendships
                .Where(f => f.UserTwoId == currentUserId.Value && f.Status == FriendshipStatus.Pending)
                .Include(f => f.UserOne)
                .OrderByDescending(f => f.RequestDate)
                .ToListAsync();

            // 3) Friends (accepted both sides)
            var friends = await _context.Friendships
                .Where(f => (f.UserOneId == currentUserId.Value || f.UserTwoId == currentUserId.Value)
                            && f.Status == FriendshipStatus.Accepted)
                .Include(f => f.UserOne)
                .Include(f => f.UserTwo)
                .ToListAsync();

            // 4) Pending community invitations
            var communityInvitations = await _context.CommunityInvitations
                .Where(inv => inv.InviteeUserId == currentUserId.Value && inv.Status == "Pending")
                .Include(inv => inv.Community)
                .Include(inv => inv.Inviter)
                .OrderByDescending(inv => inv.DateSent)
                .ToListAsync();

            // --- 5) NEW: General Notifications (unread only) ---
            var generalNotifications = await _context.Notifications
                .Where(n => n.UserId == currentUserId.Value && !n.IsRead)
                .OrderByDescending(n => n.DateCreated)
                .ToListAsync();
            // --- END ---

            var viewModel = new CommunicationHubViewModel
            {
                PendingTeamInvitations = teamInvitations,
                PendingFriendRequests = friendRequests,
                Friends = friends,
                PendingCommunityInvitations = communityInvitations,
                GeneralNotifications = generalNotifications // <-- ADDED
            };

            return View("~/Views/CommunicationHub/Communication.cshtml", viewModel);
        }

        // --- NEW ACTION ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == currentUserId.Value);

            if (notification != null)
            {
                notification.IsRead = true;
                _context.Notifications.Update(notification);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        // --- NEW ACTION ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            var notifications = await _context.Notifications
                .Where(n => n.UserId == currentUserId.Value && !n.IsRead)
                .ToListAsync();

            foreach (var n in notifications)
            {
                n.IsRead = true;
            }

            _context.Notifications.UpdateRange(notifications);
            await _context.SaveChangesAsync();
            
            return RedirectToAction("Index");
        }
    }
}