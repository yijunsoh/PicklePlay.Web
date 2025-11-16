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
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            var viewModel = new SearchViewModel
            {
                Players = await GetPlayersPaginatedAsync(1),
                Games = await GetGamesAsync("", "", 1, currentUserId),
                Competitions = await GetCompetitionsAsync("", "", 1)
            };

            return View("~/Views/Home/Search.cshtml", viewModel);
        }

        // ========== PLAYER METHODS ==========
        // GET: /Search/InitialPlayers
        public async Task<IActionResult> GetInitialPlayers(int page = 1)
        {
            var players = await GetPlayersPaginatedAsync(page);
            return PartialView("_PlayerResults", players);
        }

        // GET: /Search/Players
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

            return Json(new
            {
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

        private async Task<List<User>> GetPlayersAsync(string searchTerm = "", string filter = "", string gender = "", int page = 1)
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

        // ========== COMPETITION METHODS ==========
        // GET: /Search/Competitions
        public async Task<IActionResult> GetCompetitions(string searchTerm = "", string filter = "", int page = 1)
        {
            var competitions = await GetCompetitionsAsync(searchTerm, filter, page);
            return PartialView("_CompetitionResults", competitions);
        }

        // GET: /Search/InitialCompetitions
        public async Task<IActionResult> GetInitialCompetitions(int page = 1)
        {
            var competitions = await GetCompetitionsAsync("", "", page);
            return PartialView("_CompetitionResults", competitions);
        }

        // GET: /Search/CompetitionCount
        public async Task<IActionResult> GetCompetitionCount(string searchTerm = "", string filter = "")
        {
            var totalCount = await GetTotalCompetitionsCountAsync(searchTerm, filter);
            var totalPages = (int)System.Math.Ceiling(totalCount / (double)PAGE_SIZE);

            return Json(new
            {
                totalCount = totalCount,
                totalPages = totalPages,
                pageSize = PAGE_SIZE
            });
        }

        private async Task<List<Schedule>> GetCompetitionsAsync(string searchTerm = "", string filter = "", int page = 1)
        {
            var query = _context.Schedules
                .Include(s => s.Competition)
                .Include(s => s.Community)
                .Where(s => s.ScheduleType == ScheduleType.Competition && s.Status != ScheduleStatus.Cancelled)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(s => s.GameName != null && s.GameName.Contains(searchTerm));
            }

            switch (filter)
            {
                case "upcoming":
                    query = query.Where(s => s.StartTime > System.DateTime.Now)
                                .OrderBy(s => s.StartTime);
                    break;
                case "registration":
                    query = query.Where(s => s.RegOpen <= System.DateTime.Now && s.RegClose >= System.DateTime.Now)
                                .OrderBy(s => s.StartTime);
                    break;
                default:
                    query = query.OrderBy(s => s.StartTime);
                    break;
            }

            return await query
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToListAsync();
        }

        private async Task<int> GetTotalCompetitionsCountAsync(string searchTerm = "", string filter = "")
        {
            var query = _context.Schedules
                .Where(s => s.ScheduleType == ScheduleType.Competition && s.Status != ScheduleStatus.Cancelled)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(s => s.GameName != null && s.GameName.Contains(searchTerm));
            }

            switch (filter)
            {
                case "upcoming":
                    query = query.Where(s => s.StartTime > System.DateTime.Now);
                    break;
                case "registration":
                    query = query.Where(s => s.RegOpen <= System.DateTime.Now && s.RegClose >= System.DateTime.Now);
                    break;
            }

            return await query.CountAsync();
        }

        // ========== COMMUNITY METHODS ==========
        // GET: /Search/Communities
        public async Task<IActionResult> GetCommunities(string searchTerm = "", string filter = "", int page = 1)
        {
            var communities = await GetCommunitiesAsync(searchTerm, filter, page);
            return PartialView("_CommunityResults", communities);
        }

        // GET: /Search/InitialCommunities
        public async Task<IActionResult> GetInitialCommunities(int page = 1)
        {
            var communities = await GetCommunitiesAsync("", "", page);
            return PartialView("_CommunityResults", communities);
        }

        // GET: /Search/CommunityCount
        public async Task<IActionResult> GetCommunityCount(string searchTerm = "", string filter = "")
        {
            var totalCount = await GetTotalCommunitiesCountAsync(searchTerm, filter);
            var totalPages = (int)System.Math.Ceiling(totalCount / (double)PAGE_SIZE);

            return Json(new
            {
                totalCount = totalCount,
                totalPages = totalPages,
                pageSize = PAGE_SIZE
            });
        }

        private async Task<List<Community>> GetCommunitiesAsync(string searchTerm = "", string filter = "", int page = 1)
        {
            var query = _context.Communities
                .Include(c => c.Creator)
                .Include(c => c.Memberships)
                .Where(c => c.Status == "Active")
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(c =>
                    c.CommunityName.Contains(searchTerm) ||
                    (c.Description != null && c.Description.Contains(searchTerm))
                );
            }

            switch (filter)
            {
                case "newest":
                    query = query.OrderByDescending(c => c.CreatedDate);
                    break;
                case "largest":
                    query = query.OrderByDescending(c => c.Memberships.Count(m => m.Status == "Active"));
                    break;
                case "active":
                    query = query.OrderByDescending(c => c.LastActivityDate ?? c.CreatedDate);
                    break;
                default:
                    query = query.OrderByDescending(c => c.CreatedDate);
                    break;
            }

            return await query
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToListAsync();
        }

        private async Task<int> GetTotalCommunitiesCountAsync(string searchTerm = "", string filter = "")
        {
            var query = _context.Communities
                .Where(c => c.Status == "Active")
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(c =>
                    c.CommunityName.Contains(searchTerm) ||
                    (c.Description != null && c.Description.Contains(searchTerm))
                );
            }

            return await query.CountAsync();
        }

        // ========== GAME METHODS ==========
        // GET: /Search/Games
        public async Task<IActionResult> GetGames(string searchTerm = "", string filter = "", int page = 1)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var games = await GetGamesAsync(searchTerm, filter, page, currentUserId);
            return PartialView("_GameResults", games);
        }

        // GET: /Search/InitialGames
        public async Task<IActionResult> GetInitialGames(int page = 1)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var games = await GetGamesAsync("", "", page, currentUserId);
            return PartialView("_GameResults", games);
        }

        // GET: /Search/GameCount
        public async Task<IActionResult> GetGameCount(string searchTerm = "", string filter = "")
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var totalCount = await GetTotalGamesCountAsync(searchTerm, filter, currentUserId);
            var totalPages = (int)System.Math.Ceiling(totalCount / (double)PAGE_SIZE);

            return Json(new
            {
                totalCount = totalCount,
                totalPages = totalPages,
                pageSize = PAGE_SIZE
            });
        }

        private async Task<List<Schedule>> GetGamesAsync(string searchTerm = "", string filter = "", int page = 1, int? currentUserId = null)
        {
            // Get private communities where user is an active member
            var userPrivateCommunityIds = currentUserId.HasValue
                ? await _context.CommunityMembers
                    .Where(cm => cm.UserId == currentUserId.Value &&
                                cm.Status == "Active" &&
                                cm.Community.CommunityType == "Private")
                    .Select(cm => cm.CommunityId)
                    .ToListAsync()
                : new List<int>();

            var query = _context.Schedules
                .Include(s => s.Participants)
                .Include(s => s.Community)
                .Where(s => s.ScheduleType == ScheduleType.OneOff &&
                           s.Status != ScheduleStatus.Cancelled &&
                           // Show public games OR games from private communities where user is a member
                           (s.Privacy == Privacy.Public ||
                            (s.CommunityId.HasValue && userPrivateCommunityIds.Contains(s.CommunityId.Value))))
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(s =>
                    (s.GameName != null && s.GameName.Contains(searchTerm)) ||
                    (s.Location != null && s.Location.Contains(searchTerm))
                );
            }

            switch (filter)
            {
                case "upcoming":
                    query = query.Where(s => s.StartTime > System.DateTime.Now)
                                .OrderBy(s => s.StartTime);
                    break;
                case "free":
                    query = query.Where(s => s.FeeType == FeeType.Free || s.FeeType == FeeType.None)
                                .OrderBy(s => s.StartTime);
                    break;
                case "paid":
                    query = query.Where(s => s.FeeType == FeeType.PerPerson || s.FeeType == FeeType.AutoSplitTotal)
                                .OrderBy(s => s.StartTime);
                    break;
                default:
                    query = query.OrderBy(s => s.StartTime);
                    break;
            }

            return await query
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToListAsync();
        }

        private async Task<int> GetTotalGamesCountAsync(string searchTerm = "", string filter = "", int? currentUserId = null)
        {
            // Get private communities where user is an active member
            var userPrivateCommunityIds = currentUserId.HasValue
                ? await _context.CommunityMembers
                    .Where(cm => cm.UserId == currentUserId.Value &&
                                cm.Status == "Active" &&
                                cm.Community.CommunityType == "Private")
                    .Select(cm => cm.CommunityId)
                    .ToListAsync()
                : new List<int>();

            var query = _context.Schedules
                .Where(s => s.ScheduleType == ScheduleType.OneOff &&
                           s.Status != ScheduleStatus.Cancelled &&
                           (s.Privacy == Privacy.Public ||
                            (s.CommunityId.HasValue && userPrivateCommunityIds.Contains(s.CommunityId.Value))))
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(s =>
                    (s.GameName != null && s.GameName.Contains(searchTerm)) ||
                    (s.Location != null && s.Location.Contains(searchTerm))
                );
            }

            switch (filter)
            {
                case "upcoming":
                    query = query.Where(s => s.StartTime > System.DateTime.Now);
                    break;
                case "free":
                    query = query.Where(s => s.FeeType == FeeType.Free || s.FeeType == FeeType.None);
                    break;
                case "paid":
                    query = query.Where(s => s.FeeType == FeeType.PerPerson || s.FeeType == FeeType.AutoSplitTotal);
                    break;
            }

            return await query.CountAsync();
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

        // POST: /Search/JoinCommunity
        [HttpPost]
        public async Task<IActionResult> JoinCommunity(int communityId)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
            {
                return Json(new { success = false, message = "Please log in to join communities" });
            }

            var community = await _context.Communities
                .Include(c => c.Memberships)
                .FirstOrDefaultAsync(c => c.CommunityId == communityId);

            if (community == null)
            {
                return Json(new { success = false, message = "Community not found" });
            }

            var existingMembership = community.Memberships
                .FirstOrDefault(m => m.UserId == currentUserId.Value);

            if (existingMembership != null)
            {
                return Json(new
                {
                    success = false,
                    message = existingMembership.Status == "Pending"
                        ? "Join request is pending approval"
                        : "You are already a member of this community"
                });
            }

            if (community.CommunityType == "Private")
            {
                var membership = new CommunityMember
                {
                    CommunityId = communityId,
                    UserId = currentUserId.Value,
                    JoinDate = System.DateTime.UtcNow,
                    Status = "Pending",
                    CommunityRole = "Member"
                };

                _context.CommunityMembers.Add(membership);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Join request sent. Waiting for approval.",
                    requiresApproval = true
                });
            }
            else
            {
                var membership = new CommunityMember
                {
                    CommunityId = communityId,
                    UserId = currentUserId.Value,
                    JoinDate = System.DateTime.UtcNow,
                    Status = "Active",
                    CommunityRole = "Member"
                };

                _context.CommunityMembers.Add(membership);
                community.LastActivityDate = System.DateTime.UtcNow;
                _context.Communities.Update(community);

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Successfully joined the community!",
                    requiresApproval = false
                });
            }
        }

        // GET: /Search/GetCommunityDetails
[HttpGet]
public async Task<IActionResult> GetCommunityDetails(int communityId)
{
    try
    {
        var currentUserId = HttpContext.Session.GetInt32("UserId");
        
        var community = await _context.Communities
            .Include(c => c.Creator)
            .Include(c => c.Memberships)
            .FirstOrDefaultAsync(c => c.CommunityId == communityId && c.Status == "Active");

        if (community == null)
        {
            return Json(new { success = false, message = "Community not found" });
        }

        var isMember = currentUserId.HasValue && 
                      community.Memberships.Any(m => m.UserId == currentUserId.Value && m.Status == "Active");
                      
        var isPending = currentUserId.HasValue && 
                       community.Memberships.Any(m => m.UserId == currentUserId.Value && m.Status == "Pending");

        var result = new
        {
            communityId = community.CommunityId,
            communityName = community.CommunityName,
            description = community.Description ?? "No description provided",
            // FIX: Correct property name - CommunityLocation
            communityLocation = community.CommunityLocation ?? "Location not specified",
            communityType = community.CommunityType,
            communityPic = community.CommunityPic,
            creatorName = community.Creator?.Username ?? "Unknown",
            createdDate = community.CreatedDate,
            memberCount = community.Memberships.Count(m => m.Status == "Active"),
            lastActivityDate = community.LastActivityDate,
            isMember = isMember,
            isPending = isPending
        };

        return Json(new { success = true, data = result });
    }
    catch (Exception ex)
    {
        // Log the actual exception for debugging
        Console.WriteLine($"Error loading community details: {ex.Message}");
        return Json(new { success = false, message = "Error loading community details" });
    }
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