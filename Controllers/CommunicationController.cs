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

            // 1. Get pending team invitations for the current user
            var teamInvitations = await _context.TeamInvitations
                .Where(inv => inv.InviteeUserId == currentUserId.Value && inv.Status == InvitationStatus.Pending)
                .Include(inv => inv.Team)
                .Include(inv => inv.Inviter)
                .OrderByDescending(inv => inv.DateSent)
                .ToListAsync();

            // 2. Get pending friend requests for the current user
            var friendRequests = await _context.Friendships
                .Where(f => f.UserTwoId == currentUserId.Value && f.Status == FriendshipStatus.Pending)
                .Include(f => f.UserOne) // UserOne is the person who sent the request
                .OrderByDescending(f => f.RequestDate)
                .ToListAsync();

            // 3. Get all accepted friends
            var friends = await _context.Friendships
                .Where(f => (f.UserOneId == currentUserId.Value || f.UserTwoId == currentUserId.Value) && f.Status == FriendshipStatus.Accepted)
                .Include(f => f.UserOne)
                .Include(f => f.UserTwo)
                .ToListAsync();

            var viewModel = new CommunicationHubViewModel
            {
                PendingTeamInvitations = teamInvitations,
                PendingFriendRequests = friendRequests,
                Friends = friends
            };

            return View("~/Views/CommunicationHub/Communication.cshtml", viewModel);
        }
    }
}