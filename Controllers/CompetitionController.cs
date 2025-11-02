using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using Microsoft.EntityFrameworkCore; // Needed for Include
using System.Linq; // Needed for Select

namespace PicklePlay.Controllers
{
    public class CompetitionController : Controller
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly ApplicationDbContext _context; // Inject DbContext for Include

        public CompetitionController(IScheduleRepository scheduleRepository, ApplicationDbContext context)
        {
            _scheduleRepository = scheduleRepository;
            _context = context;
        }

        // GET: /Competition/Listing
        
public IActionResult Listing()
{
    // Fetch SCHEDULES of type Competition, status Active, including Competition data
    var activeCompetitions = _context.Schedules
                              .Include(s => s.Competition) // Eager load Competition details
                              .Where(s => s.ScheduleType == ScheduleType.Competition
                                       && s.Status == ScheduleStatus.Active // <-- ADD THIS FILTER
                                       && s.Competition != null)
                              .ToList(); // Fetch the data

    // Pass the filtered list of Schedule objects directly to the view
    return View(activeCompetitions);
}

        // GET: /Competition/CompDetails/{id}
        public IActionResult CompDetails(int id)
        {
            // Fetch the specific SCHEDULE, ensuring it's a Competition and include Competition data
            var schedule = _context.Schedules
                                  .Include(s => s.Competition) // Eager load Competition details
                                  .FirstOrDefault(s => s.ScheduleId == id
                                                   && s.ScheduleType == ScheduleType.Competition); // Don't require s.Competition != null here, might view pending

            if (schedule == null)
            {
                return NotFound();
            }

            // Check if setup is actually complete, maybe add a flag to ViewModel later
            if (schedule.Status == ScheduleStatus.PendingSetup)
            {
                // Optionally add a message indicating setup isn't complete
                ViewData["SetupPending"] = true;
            }

            // Pass the Schedule object directly to the view
            return View(schedule);
        }
        
        

         // Placeholder ViewModel for Listing example
        public class CompetitionListViewModel
        {
            public int ScheduleId { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? Location { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public ScheduleStatus? Status { get; set; }
            public decimal? FeeAmount { get; set; }
            public CompetitionFormat Format { get; set; }
            // public int? CommunityId { get; set; }
            // public string? CreatedBy { get; set; }
        }
    }
}