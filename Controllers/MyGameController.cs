using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

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
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        public async Task<IActionResult> MyGame()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Auth"); 
            }

            var now = DateTime.Now;

            // Get ALL player participations for the user, including Cancelled ones
            var userParticipations = await _context.ScheduleParticipants
                .Where(p => p.UserId == currentUserId.Value && p.Role == ParticipantRole.Player)
                .Include(p => p.Schedule)
                    .ThenInclude(s => s!.Participants) 
                .ToListAsync();

            // *** FIX: Get ALL bookmarks for the user ***
            var userBookmarks = await _context.Bookmarks
                .Where(b => b.UserId == currentUserId.Value)
                .Include(b => b.Schedule)
                    .ThenInclude(s => s!.Participants)
                .ToListAsync();

            // --- *** THIS PART IS ESSENTIAL *** ---
            var endorsedGameIds = await _context.Endorsements
                .Where(e => e.GiverUserId == currentUserId.Value)
                .Select(e => e.ScheduleId)
                .Distinct()
                .ToListAsync();
            
            // This passes the list to the _GameCard partial
            ViewBag.EndorsedGameIds = endorsedGameIds;
            // --- *** END OF ESSENTIAL PART *** ---

            var viewModel = new MyGameViewModel
            {
                // Active Games (Correct)
                ActiveGames = userParticipations
                    .Where(p => p.Schedule != null &&                          
                                p.Schedule.EndTime.HasValue &&                
                                p.Schedule.EndTime.Value >= now &&
                                (p.Status == ParticipantStatus.Confirmed ||
                                 p.Status == ParticipantStatus.PendingPayment ||
                                 p.Status == ParticipantStatus.OnHold))
                    .Select(p => p.Schedule!)
                    .OrderBy(s => s.StartTime)
                    .ToList(),

                // History Games (Correct)
                HistoryGames = userParticipations
                    .Where(p => p.Schedule != null &&                          
                                p.Schedule.EndTime.HasValue &&                
                                p.Schedule.EndTime.Value < now &&
                                p.Status == ParticipantStatus.Confirmed)
                    .Select(p => p.Schedule!)
                    .OrderByDescending(s => s.StartTime)
                    .ToList(),

                // *** FIX: Add logic for Hidden Games ***
                HiddenGames = userParticipations
                    .Where(p => p.Schedule != null &&
                                p.Status == ParticipantStatus.Cancelled) // Filter by the "Cancelled" status
                    .Select(p => p.Schedule!)
                    .OrderByDescending(s => s.StartTime)
                    .ToList(),

                // *** FIX: Add logic for Bookmarked Games ***
                BookmarkedGames = userBookmarks
                    .Where(b => b.Schedule != null) // Filter out any bookmarks for deleted schedules
                    .Select(b => b.Schedule!)
                    .OrderBy(s => s.StartTime)
                    .ToList()
            };

            return View("~/Views/Schedule/MyGame.cshtml", viewModel);
        }
    }
}