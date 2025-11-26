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

            // 1. Get ALL player participations (Joined, Pending, Quit, etc.)
            // We Include Schedule so we can check dates and status
            var userParticipations = await _context.ScheduleParticipants
                .Where(p => p.UserId == currentUserId.Value && p.Role == ParticipantRole.Player)
                .Include(p => p.Schedule)
                    .ThenInclude(s => s!.Participants) // Needed for player counts in cards
                .ToListAsync();

            // 2. Get Bookmarks
            var userBookmarks = await _context.Bookmarks
                .Where(b => b.UserId == currentUserId.Value)
                .Include(b => b.Schedule)
                    .ThenInclude(s => s!.Participants)
                .ToListAsync();

            // 3. Get Endorsements (Essential for UI buttons)
            var endorsedGameIds = await _context.Endorsements
                .Where(e => e.GiverUserId == currentUserId.Value)
                .Select(e => e.ScheduleId)
                .Distinct()
                .ToListAsync();
            
            ViewBag.EndorsedGameIds = endorsedGameIds;

            // 4. Sort data into lists
            var viewModel = new MyGameViewModel
            {
                // Active: Future + Valid Status + Schedule NOT Cancelled
                ActiveGames = userParticipations
                    .Where(p => p.Schedule != null &&                          
                                p.Schedule.EndTime.HasValue &&                
                                p.Schedule.EndTime.Value >= now &&
                                p.Schedule.Status != ScheduleStatus.Cancelled && // Don't show cancelled here
                                (p.Status == ParticipantStatus.Confirmed ||
                                 p.Status == ParticipantStatus.PendingPayment ||
                                 p.Status == ParticipantStatus.OnHold))
                    .Select(p => p.Schedule!)
                    .OrderBy(s => s.StartTime)
                    .ToList(),

                // History: Past + Confirmed + Schedule NOT Cancelled
                HistoryGames = userParticipations
                    .Where(p => p.Schedule != null &&                          
                                p.Schedule.EndTime.HasValue &&                
                                p.Schedule.EndTime.Value < now &&
                                p.Schedule.Status != ScheduleStatus.Cancelled && // Don't show cancelled here
                                p.Status == ParticipantStatus.Confirmed)
                    .Select(p => p.Schedule!)
                    .OrderByDescending(s => s.StartTime)
                    .ToList(),

                // Hidden: (I Quit) OR (Organizer Cancelled AND I was Confirmed)
                HiddenGames = userParticipations
                    .Where(p => p.Schedule != null && 
                           (
                               // Case 1: I voluntarily cancelled/quit
                               p.Status == ParticipantStatus.Cancelled 
                               ||
                               // Case 2: Organizer cancelled the game
                               // BUT I must have been Confirmed to see it (ignores Pending/OnHold)
                               (p.Schedule.Status == ScheduleStatus.Cancelled && p.Status == ParticipantStatus.Confirmed)
                           ))
                    .Select(p => p.Schedule!)
                    .OrderByDescending(s => s.StartTime)
                    .ToList(),

                // Bookmarks: Standard logic
                BookmarkedGames = userBookmarks
                    .Where(b => b.Schedule != null)
                    .Select(b => b.Schedule!)
                    .OrderBy(s => s.StartTime)
                    .ToList()
            };

            return View("~/Views/Schedule/MyGame.cshtml", viewModel);
        }
    }
}