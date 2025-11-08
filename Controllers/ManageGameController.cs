using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    public class ManageGameController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ManageGameController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        private async Task<bool> IsUserOrganizer(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return false;

            return await _context.ScheduleParticipants
                .AnyAsync(p => p.ScheduleId == scheduleId &&
                               p.UserId == currentUserId.Value &&
                               p.Role == ParticipantRole.Organizer);
        }

        // GET: /ManageGame/Index/5
        [HttpGet]
        public async Task<IActionResult> Index(int id)
        {
            if (!await IsUserOrganizer(id))
                return Forbid();

            var schedule = await _context.Schedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            var participants = await _context.ScheduleParticipants
                .Include(p => p.User)
                .Where(p => p.ScheduleId == id && p.Role == ParticipantRole.Player)
                .ToListAsync();

            var vm = new ManageGameViewModel
            {
                ScheduleId = id,
                GameName = schedule.GameName ?? "Manage Game",
                Participants = participants,
                RequireOrganizerApproval = schedule.RequireOrganizerApproval // <-- ADDED THIS
            };

            return View("~/Views/Schedule/ManageRequest.cshtml", vm);
        }

        // POST: /ManageGame/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int spId, ParticipantStatus newStatus)
        {
            var participant = await _context.ScheduleParticipants.FindAsync(spId);
            if (participant == null) return NotFound();

            if (!await IsUserOrganizer(participant.ScheduleId))
                return Forbid();

            if (newStatus != ParticipantStatus.Cancelled)
            {
                participant.Status = newStatus;
                _context.ScheduleParticipants.Update(participant);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", new { id = participant.ScheduleId });
        }

        // POST: /ManageGame/RemoveParticipant
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveParticipant(int spId)
        {
            var participant = await _context.ScheduleParticipants
                                        .Include(p => p.Schedule) // <-- Include Schedule
                                        .FirstOrDefaultAsync(p => p.SP_Id == spId);
                                        
            if (participant == null) return NotFound();

            if (!await IsUserOrganizer(participant.ScheduleId))
                return Forbid();

            var scheduleId = participant.ScheduleId;
            var userIdToNotify = participant.UserId;
            var gameName = participant.Schedule?.GameName ?? "a game"; // Get game name for message

            _context.ScheduleParticipants.Remove(participant);

            // --- CREATE NOTIFICATION ---
            var notification = new Notification
            {
                UserId = userIdToNotify,
                Message = $"You have been removed from the game: <strong>{gameName}</strong> by the organizer.",
                LinkUrl = Url.Action("Details", "Schedule", new { id = scheduleId }),
                IsRead = false,
                DateCreated = DateTime.Now
            };
            _context.Notifications.Add(notification);
            // --- END ---

            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Participant has been removed and notified.";
            return RedirectToAction("Index", new { id = scheduleId });
        }

        // --- NEW ACTION ---
        // POST: /ManageGame/ToggleApproval
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleApproval(int scheduleId)
        {
            if (!await IsUserOrganizer(scheduleId))
                return Forbid();

            var schedule = await _context.Schedules.FindAsync(scheduleId);
            if (schedule == null) return NotFound();

            // Flip the boolean
            schedule.RequireOrganizerApproval = !schedule.RequireOrganizerApproval;
            _context.Schedules.Update(schedule);
            await _context.SaveChangesAsync();

            if (schedule.RequireOrganizerApproval)
            {
                TempData["SuccessMessage"] = "Approval is now ON. New players will go 'On Hold'.";
            }
            else
            {
                TempData["SuccessMessage"] = "Approval is now OFF. New players will join directly.";
            }

            return RedirectToAction("Index", new { id = scheduleId });
        }
    }
}