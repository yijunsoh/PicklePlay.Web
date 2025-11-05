using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.AspNetCore.Http; // *** ADD THIS ***

namespace PicklePlay.Controllers
{
    public class CompetitionController : Controller
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor; // *** ADD THIS ***

        // *** MODIFY CONSTRUCTOR ***
        public CompetitionController(IScheduleRepository scheduleRepository, ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _scheduleRepository = scheduleRepository;
            _context = context;
            _httpContextAccessor = httpContextAccessor; // *** ADD THIS ***
        }

        // *** ADD THIS HELPER ***
        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        public IActionResult Listing()
        {
            var activeCompetitions = _context.Schedules
                                      .Include(s => s.Competition) 
                                      .Where(s => s.ScheduleType == ScheduleType.Competition
                                               && s.Status == ScheduleStatus.Active
                                               && s.Competition != null)
                                      .ToList(); 

            return View(activeCompetitions);
        }

        // *** MODIFY THIS ACTION ***
        public IActionResult CompDetails(int id)
        {
            var currentUserId = GetCurrentUserId();

            var schedule = _context.Schedules
                                  .Include(s => s.Competition)
                                  // --- ADDED INCLUDES FOR REGISTRATION TAB ---
                                  .Include(s => s.Participants) // For Staff tab
                                      .ThenInclude(p => p.User)
                                  .Include(s => s.Teams) // For Teams tab
                                      .ThenInclude(t => t.TeamMembers)
                                          .ThenInclude(tm => tm.User) // Get member details
                                  .Include(s => s.Teams) // Also include captain
                                      .ThenInclude(t => t.Captain) 
                                  // --- END OF ADDED INCLUDES ---
                                  .FirstOrDefault(s => s.ScheduleId == id
                                                   && s.ScheduleType == ScheduleType.Competition); 

            if (schedule == null)
            {
                return NotFound();
            }

            if (schedule.Status == ScheduleStatus.PendingSetup)
            {
                ViewData["SetupPending"] = true;
            }

            // --- ADDED FOR "REGISTER TEAM" BUTTON VISIBILITY ---
            if (currentUserId.HasValue)
            {
                ViewBag.HasUserRegisteredTeam = schedule.Teams.Any(t => t.CreatedByUserId == currentUserId.Value);
            }
            else
            {
                ViewBag.HasUserRegisteredTeam = false;
            }
            ViewBag.CurrentUserId = currentUserId;
            // --- END ADD ---
            
            return View(schedule);
        }
    }
}