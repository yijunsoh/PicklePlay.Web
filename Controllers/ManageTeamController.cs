using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    public class ManageTeamController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ManageTeamController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
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

        // GET: /ManageTeam/Index/5
       [HttpGet]
        public async Task<IActionResult> Index(int id)
        {
            var currentUserId = GetCurrentUserId();
            var isOrganizer = await IsUserOrganizer(id);

            var schedule = await _context.Schedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            var teams = await _context.Teams
                .Include(t => t.Captain)
                .Include(t => t.TeamMembers)
                    .ThenInclude(tm => tm.User)
                .Where(t => t.ScheduleId == id)
                .ToListAsync();

            var vm = new ManageTeamViewModel
            {
                ScheduleId = id,
                CompetitionName = schedule.GameName,
                // ⬇️ POPULATE LISTS BASED ON NEW LOGIC
                OnHoldTeams = teams.Where(t => t.Status == TeamStatus.Onhold).ToList(),
                PendingTeams = teams.Where(t => t.Status == TeamStatus.Pending).ToList(),
                ConfirmedTeams = teams.Where(t => t.Status == TeamStatus.Confirmed).ToList(),
                IsOrganizer = isOrganizer,
                IsFreeCompetition = schedule.FeeType == FeeType.Free || (schedule.FeeAmount ?? 0) == 0,
                MaxTeams = schedule.NumTeam ?? 0,
                CurrentUserId = currentUserId
            };

            return View("~/Views/Competition/ManageTeam.cshtml", vm);
        }

        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateTeamStatus(int teamId, TeamStatus newStatus)
{
    var team = await _context.Teams.FindAsync(teamId);
    if (team == null) return NotFound();

    if (!await IsUserOrganizer(team.ScheduleId))
        return Forbid();

    // Validation: Prevent accepting if ALREADY full
    var schedule = await _context.Schedules.AsNoTracking().FirstOrDefaultAsync(s => s.ScheduleId == team.ScheduleId);
    int confirmedCount = await _context.Teams.CountAsync(t => t.ScheduleId == team.ScheduleId && t.Status == TeamStatus.Confirmed);
    int maxTeams = schedule?.NumTeam ?? 0;

    // If trying to confirm/accept and it's already full
    if ((newStatus == TeamStatus.Pending || newStatus == TeamStatus.Confirmed) && confirmedCount >= maxTeams)
    {
        TempData["ErrorMessage"] = "Cannot accept team: The competition is full.";
        return RedirectToAction("Index", new { id = team.ScheduleId });
    }

    // Logic for Free Competitions
    bool isFree = schedule != null && (schedule.FeeType == FeeType.Free || (schedule.FeeAmount ?? 0) == 0);
    if (isFree && newStatus == TeamStatus.Confirmed)
    {
        team.PaymentStatusForSchedule = PaymentStatusForSchedule.Paid;
    }

    team.Status = newStatus;
    _context.Teams.Update(team);
    await _context.SaveChangesAsync(); // Save the status change first

    // ⬇️ TRIGGER AUTO-MOVE LOGIC IF A TEAM WAS CONFIRMED
    if (newStatus == TeamStatus.Confirmed)
    {
        await CheckForFullCompetition(team.ScheduleId);
    }

    return RedirectToAction("Index", new { id = team.ScheduleId });
}

// 2. UPDATE CheckForFullCompetition to be robust
private async Task CheckForFullCompetition(int scheduleId)
{
    var schedule = await _context.Schedules.AsNoTracking().FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);
    if (schedule == null) return;

    // Count confirmed teams
    int confirmedCount = await _context.Teams.CountAsync(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed);
    int maxTeams = schedule.NumTeam ?? 0;

    // If Full (or over capacity)
    if (confirmedCount >= maxTeams)
    {
        // Find all teams that are currently "Pending" (waiting for payment)
        var pendingTeams = await _context.Teams
            .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Pending)
            .ToListAsync();

        if (pendingTeams.Any())
        {
            foreach (var team in pendingTeams)
            {
                team.Status = TeamStatus.Onhold; // Move back to OnHold
            }

            _context.Teams.UpdateRange(pendingTeams);
            await _context.SaveChangesAsync();
        }
    }
}

        // POST: /ManageTeam/BulkUpdateTeamStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAcceptTeams(int scheduleId, List<int> teamIds)
        {
            if (!await IsUserOrganizer(scheduleId)) return Forbid();
                
            if (teamIds == null || !teamIds.Any())
            {
                TempData["ErrorMessage"] = "No teams selected.";
                return RedirectToAction("Index", new { id = scheduleId });
            }

            var schedule = await _context.Schedules.FindAsync(scheduleId);
            if (schedule == null) return NotFound();

            // ⬇️ VALIDATION: Check capacity
            int confirmedCount = await _context.Teams.CountAsync(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed);
            int pendingCount = await _context.Teams.CountAsync(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Pending); // Slots currently held for payment
            
            // Calculate available slots based on strict Confirmed count
            int availableSlots = (schedule.NumTeam ?? 0) - confirmedCount;

            if (availableSlots <= 0)
            {
                TempData["ErrorMessage"] = "Competition is already full. Cannot accept more teams.";
                return RedirectToAction("Index", new { id = scheduleId });
            }

            
            
            bool isFree = (schedule.FeeType == FeeType.Free || (schedule.FeeAmount ?? 0) == 0);
            
            // If Free, they go straight to Confirmed, so we MUST enforce limit strictly here.
            if (isFree && teamIds.Count > availableSlots)
            {
                TempData["ErrorMessage"] = $"Cannot accept {teamIds.Count} teams. Only {availableSlots} spots remaining.";
                return RedirectToAction("Index", new { id = scheduleId });
            }

            var targetStatus = isFree ? TeamStatus.Confirmed : TeamStatus.Pending;
            var teams = await _context.Teams
                .Where(t => t.ScheduleId == scheduleId && teamIds.Contains(t.TeamId))
                .ToListAsync();

            foreach (var team in teams)
            {
                team.Status = targetStatus;
                if (isFree && targetStatus == TeamStatus.Confirmed)
                {
                    team.PaymentStatusForSchedule = PaymentStatusForSchedule.Paid;
                }
            }

            _context.Teams.UpdateRange(teams);
            await _context.SaveChangesAsync();

            // ⬇️ NEW: Run cleanup if we moved teams to Confirmed
            if (targetStatus == TeamStatus.Confirmed)
            {
                await CheckForFullCompetition(scheduleId);
            }
            
            var statusName = isFree ? "Confirmed" : "Pending (Payment)";
            TempData["SuccessMessage"] = $"{teams.Count} teams moved to {statusName}.";
            return RedirectToAction("Index", new { id = scheduleId });
        }

        
        
                

        // POST: /ManageTeam/UpdatePaymentStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        // *** RENAMED ENUM ***
        public async Task<IActionResult> UpdatePaymentStatus(int teamId, PaymentStatusForSchedule newStatus)
        {
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return NotFound();

            if (!await IsUserOrganizer(team.ScheduleId))
                return Forbid();

            team.PaymentStatusForSchedule = newStatus;
            _context.Teams.Update(team);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { id = team.ScheduleId });
        }

        // POST: /ManageTeam/RemoveMember
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(int teamId, int userId)
        {
            var currentUserId = GetCurrentUserId();
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return NotFound();

            if (team.CreatedByUserId != currentUserId)
                return Forbid();
                
            if (team.CreatedByUserId == userId)
            {
                 TempData["ErrorMessage"] = "You cannot remove yourself. Make another member captain first.";
                 return RedirectToAction("Index", new { id = team.ScheduleId });
            }

            var member = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId);

            if (member != null)
            {
                _context.TeamMembers.Remove(member);
                await _context.SaveChangesAsync();
                // TODO: Send notification to the user
            }

            return RedirectToAction("Index", new { id = team.ScheduleId });
        }

        // POST: /ManageTeam/MakeCaptain
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakeCaptain(int teamId, int newCaptainUserId)
        {
            var currentUserId = GetCurrentUserId();
            var team = await _context.Teams.FindAsync(teamId);
            if (team == null) return NotFound();

            if (team.CreatedByUserId != currentUserId)
                return Forbid();
                
            bool isMember = await _context.TeamMembers
                .AnyAsync(tm => tm.TeamId == teamId && tm.UserId == newCaptainUserId);

            if (!isMember)
            {
                TempData["ErrorMessage"] = "User is not a member of this team.";
                return RedirectToAction("Index", new { id = team.ScheduleId });
            }

            team.CreatedByUserId = newCaptainUserId;
            _context.Teams.Update(team);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Captain updated successfully.";
            return RedirectToAction("Index", new { id = team.ScheduleId });
        }

        // FIND your AddStaff method and update it to use ParticipantRole.Staff

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStaff(int scheduleId, int userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = false, message = "Please log in." });
                }

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return Json(new { success = false, message = "Schedule not found." });
                }

                // Check if current user is the organizer/creator
                if (schedule.CreatedByUserId != currentUserId.Value)
                {
                    return Json(new { success = false, message = "Only the organizer can add staff." });
                }

                // Check if user already has a role in this schedule
                var existingParticipant = await _context.ScheduleParticipants
                    .FirstOrDefaultAsync(sp => sp.ScheduleId == scheduleId && sp.UserId == userId);

                if (existingParticipant != null)
                {
                    // Update existing participant to Staff role
                    existingParticipant.Role = ParticipantRole.Staff;
                    _context.ScheduleParticipants.Update(existingParticipant);
                }
                else
                {
                    // Create new staff participant
                    var newStaff = new ScheduleParticipant
                    {
                        ScheduleId = scheduleId,
                        UserId = userId,
                        Role = ParticipantRole.Staff,
                        Status = ParticipantStatus.Confirmed, // Staff are auto-accepted
                        JoinedDate = DateTime.UtcNow
                    };

                    await _context.ScheduleParticipants.AddAsync(newStaff);
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Staff added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in AddStaff: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveStaff(int scheduleId, int userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = false, message = "Please log in." });
                }

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return Json(new { success = false, message = "Schedule not found." });
                }

                // Check if current user is the organizer/creator
                if (schedule.CreatedByUserId != currentUserId.Value)
                {
                    return Json(new { success = false, message = "Only the organizer can remove staff." });
                }

                // Find the staff member
                var staffMember = await _context.ScheduleParticipants
                    .FirstOrDefaultAsync(sp => sp.ScheduleId == scheduleId && 
                                      sp.UserId == userId && 
                                      sp.Role == ParticipantRole.Staff);

                if (staffMember == null)
                {
                    return Json(new { success = false, message = "Staff member not found." });
                }

                // Check if user is also a player in teams
                var isPlayerInTeams = await _context.Teams
                    .Include(t => t.TeamMembers)
                    .AnyAsync(t => t.ScheduleId == scheduleId && 
                                  t.TeamMembers.Any(tm => tm.UserId == userId));

                if (isPlayerInTeams)
                {
                    // Demote to Player role instead of removing
                    staffMember.Role = ParticipantRole.Player;
                    _context.ScheduleParticipants.Update(staffMember);
                }
                else
                {
                    // Remove completely if not a player
                    _context.ScheduleParticipants.Remove(staffMember);
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Staff removed successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in RemoveStaff: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Helper method to get staff list for display
        [HttpGet]
        public async Task<IActionResult> GetStaffList(int scheduleId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = false, message = "Please log in." });
                }

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return Json(new { success = false, message = "Schedule not found." });
                }

                bool isOrganizer = schedule.CreatedByUserId == currentUserId.Value;

                var staffMembers = await _context.ScheduleParticipants
                    .Include(sp => sp.User)
                    .Where(sp => sp.ScheduleId == scheduleId && sp.Role == ParticipantRole.Staff)
                    .Select(sp => new
                    {
                        userId = sp.UserId,
                        username = sp.User!.Username,
                        email = sp.User.Email,
                        joinedDate = sp.JoinedDate,
                        canRemove = isOrganizer && sp.UserId != schedule.CreatedByUserId // Can't remove organizer
                    })
                    .ToListAsync();

                return Json(new { success = true, staff = staffMembers });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in GetStaffList: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}