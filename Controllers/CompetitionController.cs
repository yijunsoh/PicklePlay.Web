using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.AspNetCore.Http;
using PicklePlay.Models.ViewModels; // Make sure this is included for enums
using System.Security.Claims;

namespace PicklePlay.Controllers
{
    public class CompetitionController : Controller
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CompetitionController(IScheduleRepository scheduleRepository, ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _scheduleRepository = scheduleRepository;
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");

            // --- ALTERNATIVE (if you use ASP.NET Identity) ---
            // If the above line returns null, you might need this instead.
            // var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            // if (int.TryParse(userIdString, out int userId))
            // {
            //     return userId;
            // }
            // return null;
        }

        // --- ACTION 1: Listing (FIXED) ---
        public IActionResult Listing()
        {
            var currentUserId = GetCurrentUserId();
            ViewBag.CurrentUserId = currentUserId;

            // Fetch all competitions that are not drafts or cancelled
            var allCompetitions = _context.Schedules
                .Include(s => s.Competition)
                .Include(s => s.Participants) // <-- RENAMED
                .Include(s => s.Teams)
                    .ThenInclude(t => t.TeamMembers)
                .Where(s => s.ScheduleType == ScheduleType.Competition &&
                            s.Status != ScheduleStatus.PendingSetup &&
                            s.Status != ScheduleStatus.Null &&
                            s.Status != ScheduleStatus.Cancelled &&
                            s.Status != ScheduleStatus.Past && 
                            s.Status != ScheduleStatus.Quit)
                .ToList(); 

            // Pass the complete list to the view
            return View(allCompetitions);
        }

        // --- ACTION 2: CompDetails (FIXED) ---
        public IActionResult CompDetails(int id)
        {
            var currentUserId = GetCurrentUserId();

            var schedule = _context.Schedules
                                  .Include(s => s.Competition)
                                  .Include(s => s.Participants) // <-- RENAMED
                                      .ThenInclude(p => p.User)
                                  .Include(s => s.Teams)
                                      .ThenInclude(t => t.TeamMembers)
                                          .ThenInclude(tm => tm.User)
                                  .Include(s => s.Teams)
                                      .ThenInclude(t => t.Captain) 
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

            if (currentUserId.HasValue)
            {
                ViewBag.HasUserRegisteredTeam = schedule.Teams.Any(t => 
                    t.TeamMembers.Any(tm => tm.UserId == currentUserId.Value)
                );
            }
            else
            {
                ViewBag.HasUserRegisteredTeam = false;
            }
            
            ViewBag.CurrentUserId = currentUserId;
            
            return View(schedule);
        }

        // --- ACTION 3: SearchUsersForStaff (FIXED) ---
        [HttpGet]
        public async Task<IActionResult> SearchUsersForStaff(int scheduleId, string query)
        {
            var currentUserId = GetCurrentUserId();
            if (string.IsNullOrEmpty(query) || query.Length < 2)
            {
                return Json(new List<object>());
            }

            // Find users who match the query
            var users = await _context.Users
                .Where(u => u.Username.Contains(query) && u.UserId != currentUserId) // <-- RENAMED to UserId
                .Take(10)
                .ToListAsync();

            // Get IDs of users who are already participants (staff or players)
            var existingParticipantUserIds = await _context.ScheduleParticipants // <-- RENAMED
                .Where(p => p.ScheduleId == scheduleId)
                .Select(p => p.UserId)
                .ToListAsync();

            // Filter out users who are already participants
            var results = users
                .Where(u => !existingParticipantUserIds.Contains(u.UserId)) // <-- RENAMED to UserId
                .Select(u => new
                {
                    userId = u.UserId, // <-- RENAMED to UserId
                    username = u.Username,
                    profilePicture = u.ProfilePicture // Assuming your User model has this
                });

            return Json(results);
        }

        // --- ACTION 4: AddStaff (FIXED) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStaff(int scheduleId, int userId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Unauthorized();
            }

            // Check if the current user is an organizer
            bool isOrganizer = await _context.ScheduleParticipants // <-- RENAMED
                .AnyAsync(p => p.ScheduleId == scheduleId &&
                               p.UserId == currentUserId.Value &&
                               p.Role == ParticipantRole.Organizer);

            if (!isOrganizer)
            {
                return Forbid(); // Only organizers can add other staff
            }

            // Check if the user is already a participant in any role
            bool alreadyExists = await _context.ScheduleParticipants // <-- RENAMED
                .AnyAsync(p => p.ScheduleId == scheduleId && p.UserId == userId);

            if (alreadyExists)
            {
                return BadRequest(new { message = "This user is already a participant in this competition." });
            }

            var newStaff = new ScheduleParticipant // <-- RENAMED
            {
                ScheduleId = scheduleId,
                UserId = userId,
                Role = ParticipantRole.Organizer, // Add them as an Organizer
                Status = ParticipantStatus.Confirmed // Auto-confirm staff
            };

            _context.ScheduleParticipants.Add(newStaff); // <-- RENAMED
            await _context.SaveChangesAsync();

            return Json(new { message = "Staff added successfully." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(int teamId, int userId)
        {
            var currentUserId = GetCurrentUserId();
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return NotFound();

            // Only the captain can remove members
            if (team.CreatedByUserId != currentUserId)
                return Forbid();
                
            // Captain cannot remove themself
            if (team.CreatedByUserId == userId)
            {
                 TempData["ErrorMessage"] = "You cannot remove yourself. Make another member captain first.";
                 return RedirectToAction("CompDetails", new { id = team.ScheduleId, tab = "registration-tab" });
            }

            var member = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId);

            if (member != null)
            {
                _context.TeamMembers.Remove(member);
                await _context.SaveChangesAsync();
                // TODO: Send notification to the user
            }

            return RedirectToAction("CompDetails", new { id = team.ScheduleId, tab = "registration-tab" });
        }

        // POST: /Competition/MakeCaptain
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakeCaptain(int teamId, int newCaptainUserId)
        {
            var currentUserId = GetCurrentUserId();
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return NotFound();

            // Only the current captain can do this
            if (team.CreatedByUserId != currentUserId)
                return Forbid();
                
            // Check if the new captain is actually in the team
            bool isMember = await _context.TeamMembers
                .AnyAsync(tm => tm.TeamId == teamId && tm.UserId == newCaptainUserId);

            if (!isMember)
            {
                TempData["ErrorMessage"] = "User is not a member of this team.";
                return RedirectToAction("CompDetails", new { id = team.ScheduleId, tab = "registration-tab" });
            }

            team.CreatedByUserId = newCaptainUserId; // This re-assigns the "Captain"
            _context.Teams.Update(team);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Captain updated successfully.";
            return RedirectToAction("CompDetails", new { id = team.ScheduleId, tab = "registration-tab" });
        }
        
        // GET: /Competition/MakePayment
        [HttpGet]
        public async Task<IActionResult> MakePayment(int teamId)
        {
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return NotFound();
            
            // --- PAYMENT GATEWAY LOGIC ---
            // This is where you would redirect to your payment provider
            // For now, we'll just simulate a successful payment and redirect
            // back to the organizer's manage team page.
            
            // In a real app, this logic would be in a webhook or callback
            team.PaymentStatusForSchedule = PaymentStatusForSchedule.Paid;
            _context.Teams.Update(team);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Payment successful!";
            return RedirectToAction("CompDetails", new { id = team.ScheduleId, tab = "registration-tab" });
        }
    }
}