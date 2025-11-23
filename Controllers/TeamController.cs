using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace PicklePlay.Controllers
{
    public class TeamController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public TeamController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _webHostEnvironment = webHostEnvironment;
        }

        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTeam(CreateTeamViewModel vm)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Team name is required.";
                return RedirectToAction("CompDetails", "Competition", new { id = vm.ScheduleId });
            }

            // Check if user has already registered a team for this competition
            var existingTeam = await _context.Teams
                .FirstOrDefaultAsync(t => t.ScheduleId == vm.ScheduleId && t.CreatedByUserId == currentUserId.Value);

            if (existingTeam != null)
            {
                TempData["ErrorMessage"] = "You have already registered a team for this competition.";
                return RedirectToAction("CompDetails", "Competition", new { id = vm.ScheduleId });
            }

            // --- *** FIX: FETCH REQUIRED OBJECTS FIRST *** ---
            var schedule = await _context.Schedules.FindAsync(vm.ScheduleId);
            var captain = await _context.Users.FindAsync(currentUserId.Value);

            if (schedule == null || captain == null)
            {
                TempData["ErrorMessage"] = "Could not create team. Required information is missing.";
                return RedirectToAction("CompDetails", "Competition", new { id = vm.ScheduleId });
            }
            // --- *** END OF FIX *** ---


            // --- 1. Handle File Upload ---
            string? uniqueIconPath = null;
            if (vm.TeamIconFile != null && vm.TeamIconFile.Length > 0)
            {
                uniqueIconPath = await ProcessUploadedImage(vm.TeamIconFile);
            }

            // --- 2. Create the Team ---
            var newTeam = new Team
            {
                TeamName = vm.TeamName,
                TeamIconUrl = uniqueIconPath,
                Status = TeamStatus.Onhold,
                
                // --- *** FIX: ASSIGN THE FULL OBJECTS, NOT JUST IDs *** ---
                Schedule = schedule,
                Captain = captain
            };

            _context.Teams.Add(newTeam);
            await _context.SaveChangesAsync(); // Save to get the newTeam.TeamId

            // --- 3. Add the creator as the first team member ---
            var teamMember = new TeamMember
            {
                Status = TeamMemberStatus.Joined,

                // --- *** FIX: ASSIGN THE FULL OBJECTS, NOT JUST IDs *** ---
                Team = newTeam,
                User = captain // The captain is the first member
            };

            _context.TeamMembers.Add(teamMember);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Team registered successfully! Your team is pending approval.";
            return RedirectToAction("CompDetails", "Competition", new { id = vm.ScheduleId });
        }
// *** ADD THIS NEW ACTION ***
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTeam(EditTeamViewModel vm)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Team name is required to edit.";
                // Need ScheduleId to redirect back
                var teamForSchedId = await _context.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.TeamId == vm.TeamId);
                if (teamForSchedId == null) return NotFound();
                return RedirectToAction("CompDetails", "Competition", new { id = teamForSchedId.ScheduleId });
            }

            var teamToUpdate = await _context.Teams.FindAsync(vm.TeamId);
            if (teamToUpdate == null)
            {
                return NotFound();
            }

            // *** CRITICAL: Security check ***
            if (teamToUpdate.CreatedByUserId != currentUserId.Value)
            {
                TempData["ErrorMessage"] = "You are not authorized to edit this team.";
                return RedirectToAction("CompDetails", "Competition", new { id = teamToUpdate.ScheduleId });
            }

            // Handle new file upload
            if (vm.TeamIconFile != null && vm.TeamIconFile.Length > 0)
            {
                // Delete old icon if it exists
                if (!string.IsNullOrEmpty(teamToUpdate.TeamIconUrl))
                {
                    var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, teamToUpdate.TeamIconUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }
                // Save new icon
                teamToUpdate.TeamIconUrl = await ProcessUploadedImage(vm.TeamIconFile);
            }

            // Update name
            teamToUpdate.TeamName = vm.TeamName!;

            _context.Teams.Update(teamToUpdate);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Team updated successfully.";
            return RedirectToAction("CompDetails", "Competition", new { id = teamToUpdate.ScheduleId });
        }

        [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTeam(int teamId)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue) return Unauthorized();

        var team = await _context.Teams.FindAsync(teamId);
        if (team == null) return NotFound();

        // 1. Check Authorization (Must be Captain)
        if (team.CreatedByUserId != currentUserId.Value)
        {
            TempData["ErrorMessage"] = "Only the team captain can withdraw the team.";
            return RedirectToAction("CompDetails", "Competition", new { id = team.ScheduleId });
        }

        // 2. Check Status (Must be Onhold or Pending Payment)
        // They CANNOT delete if Confirmed.
        if (team.Status != TeamStatus.Onhold && team.Status != TeamStatus.Pending)
        {
            TempData["ErrorMessage"] = "Confirmed teams cannot be withdrawn. Please contact the organizer.";
            return RedirectToAction("CompDetails", "Competition", new { id = team.ScheduleId });
        }

        // 3. Delete Team (Cascade delete in DB usually handles invitations/members, 
        // but EF Core might need explicit removal depending on your config. 
        // Standard cascade on TeamId is assumed here.)
        _context.Teams.Remove(team);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Team withdrawn successfully.";
        return RedirectToAction("CompDetails", "Competition", new { id = team.ScheduleId });
    }

        private async Task<string?> ProcessUploadedImage(IFormFile imageFile)
        {
            string uploadsFolderRelative = "img/team_icons";
            string uploadsFolderAbsolute = Path.Combine(_webHostEnvironment.WebRootPath, uploadsFolderRelative);
            Directory.CreateDirectory(uploadsFolderAbsolute);

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + imageFile.FileName;
            string filePath = Path.Combine(uploadsFolderAbsolute, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(fileStream);
            }

            return $"/{uploadsFolderRelative}/{uniqueFileName}";
        }
[HttpGet]
        public async Task<IActionResult> SearchUsersForInvite(int teamId, string query)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            // 1. Fetch Team AND Schedule
            var team = await _context.Teams
                .Include(t => t.TeamMembers)
                .Include(t => t.Invitations)
                .Include(t => t.Schedule)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TeamId == teamId);

            if (team == null || team.CreatedByUserId != currentUserId.Value)
            {
                return Forbid();
            }

            var schedule = team.Schedule;

            // 2. Get excluded IDs
            var existingMemberIds = team.TeamMembers.Select(tm => tm.UserId).ToList();
            var pendingInviteIds = team.Invitations
                .Where(inv => inv.Status == InvitationStatus.Pending)
                .Select(inv => inv.InviteeUserId)
                .ToList();
            var allExcludedIds = existingMemberIds.Concat(pendingInviteIds).Distinct().ToList();

            // 3. Fetch Candidates
            var candidates = await _context.Users
                .Include(u => u.Rank)
                .Where(u => u.Username.Contains(query) && !allExcludedIds.Contains(u.UserId))
                .Take(50)
                .ToListAsync();

            var results = new List<object>();

            // 4. Check eligibility for each candidate
            foreach (var user in candidates)
            {
                // Get reason (returns NULL if eligible)
                string? ineligibilityReason = GetIneligibilityReason(user, schedule);

                results.Add(new 
                { 
                    userId = user.UserId, 
                    username = user.Username, 
                    profilePicture = user.ProfilePicture,
                    isEligible = (ineligibilityReason == null), // True if no reason returned
                    reason = ineligibilityReason
                });

                if (results.Count >= 10) break;
            }

            return Ok(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendInvite(int teamId, int inviteeUserId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            var inviter = await _context.Users.FindAsync(currentUserId.Value);
            var invitee = await _context.Users
                .Include(u => u.Rank)
                .FirstOrDefaultAsync(u => u.UserId == inviteeUserId);

            var team = await _context.Teams
                .Include(t => t.TeamMembers)
                .Include(t => t.Invitations)
                .Include(t => t.Schedule)
                .FirstOrDefaultAsync(t => t.TeamId == teamId);
                
            if (team == null || inviter == null || invitee == null)
                return BadRequest(new { message = "Invalid data." });

            if (team.CreatedByUserId != currentUserId.Value)
                return Forbid();

            if (team.TeamMembers.Count >= 2) 
                return BadRequest(new { message = "Your team is already full." });

            // ⬇️ RE-CHECK: Final security check before sending
            string? restrictionError = GetIneligibilityReason(invitee, team.Schedule);
            if (restrictionError != null)
            {
                return BadRequest(new { message = $"Cannot invite user: {restrictionError}" });
            }

            if (team.TeamMembers.Any(tm => tm.UserId == inviteeUserId))
                return BadRequest(new { message = "This user is already on your team." });

            if (team.Invitations.Any(inv => inv.InviteeUserId == inviteeUserId && inv.Status == InvitationStatus.Pending))
                return BadRequest(new { message = "This user already has a pending invitation." });

            var newInvitation = new TeamInvitation
            {
                Team = team,
                Inviter = inviter,
                Invitee = invitee,
                Status = InvitationStatus.Pending,
                DateSent = DateTime.UtcNow
            };

            _context.TeamInvitations.Add(newInvitation);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Invitation sent!" });
        }

        // ⬇️ HELPER: Returns a string reason if ineligible, or NULL if eligible
        private string? GetIneligibilityReason(User user, Schedule schedule)
        {
            // 1. Gender Check
            if (schedule.GenderRestriction != GenderRestriction.None)
            {
                string userGender = user.Gender?.Trim() ?? "";
                if (schedule.GenderRestriction == GenderRestriction.Male && 
                    !userGender.Equals("Male", StringComparison.OrdinalIgnoreCase)) 
                    return "Male only";
                
                if (schedule.GenderRestriction == GenderRestriction.Female && 
                    !userGender.Equals("Female", StringComparison.OrdinalIgnoreCase)) 
                    return "Female only";
            }

            // 2. Age Check
            if (schedule.AgeGroupRestriction != AgeGroupRestriction.None)
            {
                int userAge = 0;
                if (user.DateOfBirth.HasValue)
                {
                    var today = DateTime.Today;
                    userAge = today.Year - user.DateOfBirth.Value.Year;
                    if (user.DateOfBirth.Value.Date > today.AddYears(-userAge)) userAge--;
                }
                else
                {
                    userAge = user.Age ?? 0;
                }

                if (userAge == 0) return "Age info missing";

                if (schedule.AgeGroupRestriction == AgeGroupRestriction.Junior && userAge >= 18) return "Junior (<18) only";
                if (schedule.AgeGroupRestriction == AgeGroupRestriction.Adult && (userAge < 18 || userAge >= 60)) return "Adult (18-59) only";
                if (schedule.AgeGroupRestriction == AgeGroupRestriction.Senior && userAge < 60) return "Senior (60+) only";
            }

            // 3. Rank Check
            decimal userRating = user.Rank != null ? user.Rank.Rating : 0.000m;

            if (schedule.MinRankRestriction.HasValue && schedule.MinRankRestriction.Value > 0)
            {
                if (userRating < schedule.MinRankRestriction.Value) 
                    return $"Min Rank {schedule.MinRankRestriction.Value:0.0}";
            }

            if (schedule.MaxRankRestriction.HasValue && schedule.MaxRankRestriction.Value > 0)
            {
                if (userRating > schedule.MaxRankRestriction.Value) 
                    return $"Max Rank {schedule.MaxRankRestriction.Value:0.0}";
            }

            return null; // Eligible
        }

        // --- *** MODIFIED ACTION *** ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptInvite(int invitationId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Auth");
            }

            // *** FIX: Fetch the current user object ***
            var currentUser = await _context.Users.FindAsync(currentUserId.Value);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Auth"); // User not found
            }

            var invitation = await _context.TeamInvitations
                .Include(inv => inv.Team)
                    .ThenInclude(t => t!.TeamMembers)
                .FirstOrDefaultAsync(inv => inv.InvitationId == invitationId);

            if (invitation == null || invitation.InviteeUserId != currentUserId.Value)
            {
                TempData["ErrorMessage"] = "Invitation not found or you are not authorized.";
                return RedirectToAction("Index", "Communication");
            }

            if (invitation.Status != InvitationStatus.Pending)
            {
                TempData["ErrorMessage"] = "This invitation is no longer active.";
                return RedirectToAction("Index", "Communication");
            }

            if (invitation.Team?.TeamMembers.Count >= 2)
            {
                invitation.Status = InvitationStatus.Declined; 
                _context.TeamInvitations.Update(invitation);
                await _context.SaveChangesAsync();
                TempData["ErrorMessage"] = "Could not join team: the team is now full.";
                return RedirectToAction("Index", "Communication");
            }

            // 1. Add user to TeamMembers
            var newTeamMember = new TeamMember
            {
                // *** FIX: Assign full objects ***
                Team = invitation.Team!,
                User = currentUser,
                Status = TeamMemberStatus.Joined
            };
            _context.TeamMembers.Add(newTeamMember);

            // 2. Update invitation status
            invitation.Status = InvitationStatus.Accepted;
            _context.TeamInvitations.Update(invitation);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"You have successfully joined {invitation.Team?.TeamName}!";
            return RedirectToAction("Index", "Communication");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineInvite(int invitationId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Auth");
            }

            var invitation = await _context.TeamInvitations
                .FirstOrDefaultAsync(inv => inv.InvitationId == invitationId);

            if (invitation == null || invitation.InviteeUserId != currentUserId.Value)
            {
                TempData["ErrorMessage"] = "Invitation not found or you are not authorized.";
                return RedirectToAction("Index", "Communication");
            }

            invitation.Status = InvitationStatus.Declined;
            _context.TeamInvitations.Update(invitation);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Invitation declined.";
            return RedirectToAction("Index", "Communication");
        }

        
    }
}
        