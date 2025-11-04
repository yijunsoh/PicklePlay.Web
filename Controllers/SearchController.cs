using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    public class SearchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const int PAGE_SIZE = 10;

        public SearchController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Search
        public async Task<IActionResult> Search()
        {
            var viewModel = new SearchViewModel
            {
                Players = await GetPlayersPaginatedAsync(1),
                Competitions = await GetCompetitionsAsync()
            };

            return View("~/Views/Home/Search.cshtml", viewModel);
        }

        // GET: /Search/InitialPlayers
        public async Task<IActionResult> GetInitialPlayers(int page = 1)
        {
            var players = await GetPlayersPaginatedAsync(page);
            return PartialView("_PlayerResults", players);
        }

        // GET: /Search/Players - Enhanced search with multiple criteria
        public async Task<IActionResult> GetPlayers(string searchTerm = "", string filter = "", string gender = "", int page = 1)
        {
            var players = await GetPlayersAsync(searchTerm, filter, gender, page);
            return PartialView("_PlayerResults", players);
        }

        // GET: /Search/PlayerCount
        public async Task<IActionResult> GetPlayerCount(string searchTerm = "", string filter = "", string gender = "")
        {
            var totalCount = await GetTotalPlayersCountAsync(searchTerm, filter, gender);
            var totalPages = (int)System.Math.Ceiling(totalCount / (double)PAGE_SIZE);
            
            return Json(new { 
                totalCount = totalCount,
                totalPages = totalPages,
                pageSize = PAGE_SIZE
            });
        }

        private async Task<List<User>> GetPlayersPaginatedAsync(int page = 1)
        {
            return await _context.Users
                .OrderByDescending(u => u.CreatedDate)
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToListAsync();
        }

        // Enhanced search with multiple criteria
        private async Task<List<User>> GetPlayersAsync(string searchTerm = "", string filter = "", string gender = "", int page = 1)
        {
            var query = _context.Users.AsQueryable();

            // Search by username, email, or bio
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(u => 
                    u.Username.Contains(searchTerm) || 
                    u.Email.Contains(searchTerm) ||
                    (u.Bio != null && u.Bio.Contains(searchTerm))
                );
            }

            // Filter by gender
            if (!string.IsNullOrEmpty(gender) && gender != "all")
            {
                query = query.Where(u => u.Gender == gender);
            }

            // Apply sorting filters
            switch (filter)
            {
                case "newest":
                    query = query.OrderByDescending(u => u.CreatedDate);
                    break;
                case "active":
                    query = query.OrderByDescending(u => u.LastLogin ?? u.CreatedDate);
                    break;
                case "experienced":
                    query = query.OrderByDescending(u => u.Age ?? 0).ThenByDescending(u => u.CreatedDate);
                    break;
                case "name_asc":
                    query = query.OrderBy(u => u.Username);
                    break;
                case "name_desc":
                    query = query.OrderByDescending(u => u.Username);
                    break;
                default:
                    query = query.OrderByDescending(u => u.CreatedDate);
                    break;
            }

            return await query
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToListAsync();
        }

        private async Task<int> GetTotalPlayersCountAsync(string searchTerm = "", string filter = "", string gender = "")
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(u => 
                    u.Username.Contains(searchTerm) || 
                    u.Email.Contains(searchTerm) ||
                    (u.Bio != null && u.Bio.Contains(searchTerm))
                );
            }

            if (!string.IsNullOrEmpty(gender) && gender != "all")
            {
                query = query.Where(u => u.Gender == gender);
            }

            return await query.CountAsync();
        }

        // GET: /Search/Competitions
        public async Task<IActionResult> GetCompetitions(string searchTerm = "", string filter = "")
        {
            var competitions = await GetCompetitionsAsync(searchTerm, filter);
            return PartialView("_CompetitionResults", competitions);
        }

        // GET: /Search/InitialCompetitions
        public async Task<IActionResult> GetInitialCompetitions()
        {
            var competitions = await GetCompetitionsAsync();
            return PartialView("_CompetitionResults", competitions);
        }

        private async Task<List<Schedule>> GetCompetitionsAsync(string searchTerm = "", string filter = "")
        {
            var query = _context.Schedules
                .Include(s => s.Competition)
                .Where(s => s.ScheduleType == ScheduleType.Competition)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(s => s.GameName.Contains(searchTerm));
            }

            switch (filter)
            {
                case "upcoming":
                    query = query.Where(s => s.StartTime > System.DateTime.Now).OrderBy(s => s.StartTime);
                    break;
                case "registration":
                    query = query.Where(s => s.RegOpen <= System.DateTime.Now && s.RegClose >= System.DateTime.Now)
                                .OrderBy(s => s.StartTime);
                    break;
                default:
                    query = query.OrderBy(s => s.StartTime);
                    break;
            }

            return await query.ToListAsync();
        }

        // GET: /Search/PlayerProfile/{id}
        public async Task<IActionResult> GetPlayerProfile(int id)
        {
            var player = await _context.Users
                .Where(u => u.UserId == id)
                .Select(u => new
                {
                    u.UserId,
                    u.Username,
                    u.Email,
                    u.ProfilePicture,
                    u.PhoneNo,
                    u.Gender,
                    u.Age,
                    u.Bio,
                    u.CreatedDate
                })
                .FirstOrDefaultAsync();

            if (player == null)
            {
                return NotFound();
            }

            return Json(player);
        }
    }

    // ViewModel to pass data to the view
    public class SearchViewModel
    {
        public List<User> Players { get; set; } = new List<User>();
        public List<Schedule> Games { get; set; } = new List<Schedule>();
        public List<Schedule> Competitions { get; set; } = new List<Schedule>();
    }
}