using Microsoft.AspNetCore.Mvc;
using PicklePlay.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System; // Added

namespace PicklePlay.Controllers
{
    public class AiPartnerController : Controller
    {
        private readonly IAiPartnerService _ai;
        private readonly ILogger<AiPartnerController> _logger;
        private readonly ApplicationDbContext _context;

        public AiPartnerController(
            IAiPartnerService ai,
            ILogger<AiPartnerController> logger,
            ApplicationDbContext context)
        {
            _ai = ai;
            _logger = logger;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // get resolved identifier (now async)
            var userIdentifier = await GetCurrentUserIdentifierAsync();
            
            ViewData["AiPartnerDebugIdentifier"] = userIdentifier ?? "";

            var totalUsers = await _context.Users.CountAsync();
            if (totalUsers <= 1)
            {
                ViewBag.AnalyzedCount = totalUsers;
                ViewBag.Message = "You are the only player in the system right now!";
                return View(new List<PicklePlay.Models.ViewModels.AiSuggestionViewModel>());
            }

            ViewBag.AnalyzedCount = totalUsers > 0 ? totalUsers - 1 : 0;

            var suggestions = await _ai.SuggestPartnersAsync(userIdentifier ?? string.Empty, 5);
            return View(suggestions);
        }

        // Async resolver: prefer session-stored email -> lookup user id, then claims fallback
        private async Task<string> GetCurrentUserIdentifierAsync()
        {
            // 1) Session email (your app sets "UserEmail" in HomeController)
            if (HttpContext?.Session != null)
            {
                var email = HttpContext.Session.GetString("UserEmail");
                if (!string.IsNullOrWhiteSpace(email))
                {
                    // find user by email and return UserId as string (matches service matching logic)
                    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
                    if (user != null)
                    {
                        return user.UserId.ToString();
                    }
                }

                // also try common session id keys (if set elsewhere)
                var sid = HttpContext.Session.GetString("UserId") ?? HttpContext.Session.GetString("Id") ?? HttpContext.Session.GetString("userid");
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    // sid may be binary-corrupted; sanitize control chars
                    var clean = new string(sid.Where(c => !char.IsControl(c)).ToArray()).Trim();
                    if (!string.IsNullOrWhiteSpace(clean)) return clean;
                }
            }

            // 2) Claims fallback (if used)
            var claimId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(claimId)) return new string(claimId.Where(c => !char.IsControl(c)).ToArray()).Trim();

            var name = User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(name)) return new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

            return string.Empty;
        }
    }
}