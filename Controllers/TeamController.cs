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
                Status = TeamStatus.Pending,
                
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
    }
}