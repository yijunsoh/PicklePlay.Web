using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http; // Required for session
using System.Collections.Generic; // Required for List<T>

namespace PicklePlay.Controllers
{
    public class MyGameController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public MyGameController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            // Helper method to safely get the current user's ID from session
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        public async Task<IActionResult> MyGame()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                // If user isn't logged in, send them to the login page
                return RedirectToAction("Login", "Auth"); 
            }

            var now = DateTime.Now;

            // Get all player participations for the current user
            // We include the Schedule, and for each Schedule, we also include its Participants list
            // to be able to show the player count (e.g., "2/8")
            var userParticipations = await _context.ScheduleParticipants
                .Where(p => p.UserId == currentUserId.Value && p.Role == ParticipantRole.Player)
                .Include(p => p.Schedule)
                    .ThenInclude(s => s!.Participants) 
                .ToListAsync();

            var viewModel = new MyGameViewModel
            {
                // Active Games:
                // Games that are not null, have an end time, AND haven't ended yet
                // AND the user is in one of the active statuses.
                ActiveGames = userParticipations
                    .Where(p => p.Schedule != null &&                          
                                p.Schedule.EndTime.HasValue &&                
                                p.Schedule.EndTime.Value >= now &&
                                (p.Status == ParticipantStatus.Confirmed ||
                                 p.Status == ParticipantStatus.PendingPayment ||
                                 p.Status == ParticipantStatus.OnHold))
                    .Select(p => p.Schedule!) // Use ! to tell the compiler we know it's not null
                    .OrderBy(s => s.StartTime)
                    .ToList(),

                // History Games:
                // Games that are not null, have an end time, AND have already ended
                // AND the user was confirmed for that game.
                HistoryGames = userParticipations
                    .Where(p => p.Schedule != null &&                          
                                p.Schedule.EndTime.HasValue &&                
                                p.Schedule.EndTime.Value < now &&
                                p.Status == ParticipantStatus.Confirmed)
                    .Select(p => p.Schedule!) // Use ! to tell the compiler we know it's not null
                    .OrderByDescending(s => s.StartTime)
                    .ToList()
            };

            // Return the specific view path you requested
            return View("~/Views/Schedule/MyGame.cshtml", viewModel);
        }
    }
}

