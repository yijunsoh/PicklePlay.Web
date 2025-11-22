// FavoriteController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;
using Microsoft.AspNetCore.Authorization;
using PicklePlay.Data;

namespace PicklePlay.Controllers
{
    public class FavoriteController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FavoriteController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Get current user ID from session
        private int? GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId");
        }

        // Toggle favorite status for a player
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ToggleFavorite(int targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = true, isFavorited = false });

            var existingFavorite = await _context.Favorites
                .FirstOrDefaultAsync(f => f.UserId == currentUserId.Value && 
                                        f.TargetUserId == targetUserId);

            bool isFavorited = false;
            if (existingFavorite != null)
            {
                _context.Favorites.Remove(existingFavorite);
                isFavorited = false;
            }
            else
            {
                var favorite = new Favorite
                {
                    UserId = currentUserId.Value,
                    TargetUserId = targetUserId,
                    CreatedDate = DateTime.UtcNow
                };
                _context.Favorites.Add(favorite);
                isFavorited = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, isFavorited });
        }

        // Check if a player is favorited
        [HttpGet]
        [AllowAnonymous]
        public async Task<JsonResult> IsFavorited(int targetUserId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = true, isFavorited = false });
                }
                
                var isFavorited = await _context.Favorites
                    .AnyAsync(f => f.UserId == currentUserId.Value && f.TargetUserId == targetUserId);

                return Json(new { success = true, isFavorited });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get user's favorite players
        [HttpGet]
        [AllowAnonymous]
        public async Task<JsonResult> GetFavoritePlayers()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = true, favorites = new List<object>() });
                }

                var favoritePlayers = await _context.Favorites
                    .Where(f => f.UserId == currentUserId.Value)
                    .Include(f => f.TargetUser)
                    .Select(f => new
                    {
                        f.TargetUserId,
                        f.TargetUser.Username,
                        f.TargetUser.ProfilePicture,
                        f.CreatedDate
                    })
                    .ToListAsync();

                return Json(new { success = true, favorites = favoritePlayers });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get favorite count for a player
        [HttpGet]
        [AllowAnonymous]
        public async Task<JsonResult> GetFavoriteCount(int targetUserId)
        {
            try
            {
                var count = await _context.Favorites
                    .CountAsync(f => f.TargetUserId == targetUserId);

                return Json(new { success = true, count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get favorite statuses for multiple players (for efficiency)
        [HttpGet]
        [AllowAnonymous]
        public async Task<JsonResult> GetFavoriteStatuses(string playerIds)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (!currentUserId.HasValue || string.IsNullOrEmpty(playerIds))
                {
                    return Json(new { success = true, favoriteStatuses = new Dictionary<int, bool>() });
                }

                var ids = playerIds.Split(',').Select(int.Parse).ToList();
                var favoriteStatuses = await _context.Favorites
                    .Where(f => f.UserId == currentUserId.Value && ids.Contains(f.TargetUserId))
                    .ToDictionaryAsync(f => f.TargetUserId, f => true);

                var result = ids.ToDictionary(id => id, id => favoriteStatuses.ContainsKey(id));

                return Json(new { success = true, favoriteStatuses = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}