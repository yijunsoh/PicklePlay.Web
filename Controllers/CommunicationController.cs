using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

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

            // 4) NEW: Pending community invitations for current user
            var communityInvitations = await _context.CommunityInvitations
                .Where(inv => inv.InviteeUserId == currentUserId.Value && inv.Status == "Pending")
                .Include(inv => inv.Community)
                .Include(inv => inv.Inviter)
                .OrderByDescending(inv => inv.DateSent)
                .ToListAsync();

            var viewModel = new CommunicationHubViewModel
            {
                PendingTeamInvitations = teamInvitations,
                PendingFriendRequests = friendRequests,
                Friends = friends,
                PendingCommunityInvitations = communityInvitations
            };

            return View("~/Views/CommunicationHub/Communication.cshtml", viewModel);
        }
    }
}
