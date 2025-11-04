using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace PicklePlay.Controllers
{
    public class BookmarkController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public BookmarkController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBookmark(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Unauthorized(new { message = "You must be logged in." });
            }

            // Check if the schedule exists
            var schedule = await _context.Schedules.FindAsync(scheduleId);
            if (schedule == null)
            {
                return NotFound(new { message = "Game not found." });
            }

            // Find an existing bookmark
            var existingBookmark = await _context.Bookmarks
                .FirstOrDefaultAsync(b => b.ScheduleId == scheduleId && b.UserId == currentUserId.Value);

            if (existingBookmark == null)
            {
                // Create a new one
                var newBookmark = new Bookmark
                {
                    UserId = currentUserId.Value,
                    ScheduleId = scheduleId
                };
                _context.Bookmarks.Add(newBookmark);
                await _context.SaveChangesAsync();
                return Ok(new { isBookmarked = true });
            }
            else
            {
                // Delete the existing one
                _context.Bookmarks.Remove(existingBookmark);
                await _context.SaveChangesAsync();
                return Ok(new { isBookmarked = false });
            }
        }
    }
}
