using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PicklePlay.Web.Models;
using PicklePlay.ViewModels;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;


namespace PicklePlay.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IAuthService _authService;
    private readonly IWebHostEnvironment _environment;
    private readonly IEmailService _emailService;
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HomeController(ILogger<HomeController> logger, IAuthService authService,
                        IWebHostEnvironment environment, IEmailService emailService, ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _authService = authService;
        _environment = environment;
        _emailService = emailService;
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }


    // GET: /Home/EditProfile

    public async Task<IActionResult> EditProfile()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            TempData["ErrorMessage"] = "Please login first.";
            return RedirectToAction("Login", "Auth");
        }

        var user = await _authService.GetUserByEmailAsync(userEmail);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction("Login", "Auth");
        }

        var model = new EditProfileModel
        {
            UserId = user.UserId,
            FullName = user.Username,
            Email = user.Email,
            PhoneNumber = user.PhoneNo,
            Gender = user.Gender,
            DateOfBirth = user.DateOfBirth,
            Age = user.Age,
            Bio = user.Bio,
            CurrentProfileImagePath = user.ProfilePicture,
            Location = user.Location
        };

        return View(model);
    }

    // POST: /Home/UpdateProfile
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(EditProfileModel model)
    {
        // Client-side validation should prevent this, but as a backup
        if (!ModelState.IsValid)
        {
            // Return to the form with validation errors - DON'T redirect
            TempData["ErrorMessage"] = "Please check your input and fix all validation errors.";

            // Re-populate the model and return the view directly
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (!string.IsNullOrEmpty(userEmail))
            {
                var currentUser = await _authService.GetUserByEmailAsync(userEmail);
                if (currentUser != null)
                {
                    // Re-populate any missing data
                    model.UserId = currentUser.UserId;
                    model.CurrentProfileImagePath = currentUser.ProfilePicture;
                    model.Email = currentUser.Email;// Ensure email is set
                    model.Location = currentUser.Location;
                }
            }

            return View("EditProfile", model);
        }

        try
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "Please login first.";
                return RedirectToAction("Login", "Auth");
            }

            var currentUser = await _authService.GetUserByEmailAsync(userEmail);
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Login", "Auth");
            }

            // Additional server-side validation
            if (string.IsNullOrEmpty(model.FullName?.Trim()))
            {
                ModelState.AddModelError("FullName", "Full name is required.");
                return View("EditProfile", model);
            }

            if (model.DateOfBirth.HasValue)
            {
                var age = DateTime.Today.Year - model.DateOfBirth.Value.Year;
                if (model.DateOfBirth.Value.Date > DateTime.Today.AddYears(-age))
                    age--;

                if (age < 18)
                {
                    ModelState.AddModelError("DateOfBirth", "You must be at least 18 years old.");
                    return View("EditProfile", model);
                }
            }

            string? profileImagePath = model.CurrentProfileImagePath;

            // Handle profile image upload
            if (model.ProfileImage != null && model.ProfileImage.Length > 0)
            {
                profileImagePath = await SaveProfileImageAsync(model.ProfileImage, currentUser.UserId);
            }

            // Update user profile
            var success = await _authService.UpdateUserProfileAsync(
                currentUser.UserId,
                model.FullName.Trim(), // Ensure trimmed
                currentUser.Email, // Always use original email
                model.PhoneNumber ?? currentUser.PhoneNo,
                model.Gender ?? currentUser.Gender,
                model.DateOfBirth ?? currentUser.DateOfBirth,
                model.Bio ?? currentUser.Bio,
                profileImagePath,
                model.Location
            );

            if (success)
            {
                HttpContext.Session.SetString("UserName", model.FullName.Trim());
                HttpContext.Session.SetString("ProfileImagePath", profileImagePath ?? "");
                TempData["SuccessMessage"] = "Profile updated successfully!";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to update profile. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            TempData["ErrorMessage"] = "An error occurred while updating your profile.";
        }

        return RedirectToAction("EditProfile");
    }

    // REMOVE or COMMENT OUT this entire method:
    // private async Task SendEmailChangeVerification(int userId, string newEmail)
    // {
    //     ... existing code ...
    // }

    // private async Task SendEmailChangeVerification(int userId, string newEmail)
    // {
    //     var user = await _authService.GetUserByIdAsync(userId);
    //     if (user == null) return;

    //     // Generate verification token for email change
    //     user.GenerateEmailVerificationToken();

    //     // Update user with new token (save to database)
    //     await _authService.UpdateUserProfileAsync(
    //         userId, 
    //         user.Username, 
    //         user.Email, // Keep original email for now
    //         user.PhoneNo, 
    //         user.Gender, 
    //         user.DateOfBirth, 
    //         user.Bio, 
    //         user.ProfilePicture
    //     );

    //     // Build verification link using the existing VerifyEmail function
    //     var verificationLink = Url.Action("VerifyEmailChange", "Auth", 
    //         new { userId = user.UserId, token = user.EmailVerificationToken, newEmail = newEmail }, 
    //         Request.Scheme);

    //     if (string.IsNullOrEmpty(verificationLink))
    //     {
    //         _logger.LogError("Failed to generate verification link for email change");
    //         return;
    //     }

    //     // Send verification email to new email address
    //     var html = $@"
    //     <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    //         <h2 style='color: #A1D336; text-align: center;'>Verify Your New Email Address</h2>
    //         <p>Hello {System.Net.WebUtility.HtmlEncode(user.Username)},</p>
    //         <p>You have requested to change your PicklePlay account email to this address. Please verify this email by clicking the button below:</p>

    //         <div style='text-align: center; margin: 30px 0;'>
    //             <a href='{verificationLink}' style='
    //                 background-color: #A1D336; 
    //                 color: white; 
    //                 padding: 15px 30px; 
    //                 text-decoration: none; 
    //                 border-radius: 25px; 
    //                 font-weight: bold;
    //                 font-size: 16px;
    //                 display: inline-block;
    //                 border: none;
    //                 cursor: pointer;'>
    //                 Verify New Email
    //             </a>
    //         </div>

    //         <p>Or copy and paste this link in your browser:</p>
    //         <p style='
    //             background-color: #f8f9fa; 
    //             padding: 15px; 
    //             border-radius: 5px; 
    //             word-break: break-all; 
    //             font-family: monospace;
    //             border-left: 4px solid #A1D336;'>
    //             {verificationLink}
    //         </p>

    //         <div style='
    //             background-color: #fff3cd; 
    //             border: 1px solid #ffeaa7; 
    //             border-radius: 5px; 
    //             padding: 15px; 
    //             margin: 20px 0;'>
    //             <strong>⚠️ Important:</strong> This link will expire in 5 minutes.
    //         </div>

    //         <p>If you did not request this change, please ignore this email.</p>

    //         <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;'>
    //         <p style='color: #666; font-size: 12px; text-align: center;'>
    //             This is an automated message from PicklePlay. Please do not reply to this email.
    //         </p>
    //     </div>";

    //     await _emailService.SendEmailAsync(newEmail, "Verify Your New PicklePlay Email", html);
    // }

    private async Task<string?> SaveProfileImageAsync(IFormFile profileImage, int userId)
    {
        try
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "profiles");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // Validate file type
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(profileImage.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(fileExtension))
            {
                throw new Exception("Invalid file type. Only JPG, PNG, and GIF are allowed.");
            }

            // Validate file size (5MB max)
            if (profileImage.Length > 5 * 1024 * 1024)
            {
                throw new Exception("File size too large. Maximum size is 5MB.");
            }

            var fileName = $"profile_{userId}_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await profileImage.CopyToAsync(stream);
            }

            return $"/images/profiles/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile image");
            return null; // Return null instead of throwing
        }
    }

    // Other existing methods remain the same...
    [HttpGet]
    public IActionResult Community()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            TempData["ErrorMessage"] = "Please login first.";
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    [HttpGet]
    public IActionResult CommunityPages(int? id)
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            TempData["ErrorMessage"] = "Please login first.";
            return RedirectToAction("Login", "Auth");
        }
        ViewBag.CommunityId = id;
        return View();
    }

    public IActionResult Index()
    {
        return RedirectToAction("Login", "Auth");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    // This action will be called by the partial view
    [HttpGet]
    public async Task<IActionResult> EndorsementSection(int userId)
    {
        // Get all endorsements for the specified user
        var allReceived = await _context.Endorsements
            .Where(e => e.ReceiverUserId == userId)
            .ToListAsync();

        // Process Skills
        var allSkills = allReceived
            .Where(e => e.Skill != SkillEndorsement.None)
            .GroupBy(e => e.Skill)
            .Select(g => new EndorsementSummary { EndorsementName = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .ToList();

        // Process Personalities
        var allPersonalities = allReceived
            .Where(e => e.Personality != PersonalityEndorsement.None)
            .GroupBy(e => e.Personality)
            .Select(g => new EndorsementSummary { EndorsementName = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .ToList();

        var vm = new ProfileEndorsementViewModel
        {
            AllSkills = allSkills,
            TopSkills = allSkills.Take(3).ToList(),
            AllPersonalities = allPersonalities,
            TopPersonalities = allPersonalities.Take(3).ToList()
        };

        return PartialView("~/Views/Shared/_EndorsementSection.cshtml", vm);
    }

    // ADD this method to your HomeController

[HttpGet]
public async Task<IActionResult> GetUserProfilePreview(int userId)
{
    try
    {
        // 1. DIRECTLY GET CURRENT USER ID (Bypass helper to avoid null/0 issues)
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        
        Console.WriteLine($"[DEBUG] ProfilePreview - Target: {userId}, Viewer: {currentUserId}");

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return Json(new { success = false, message = "User not found" });

        Console.WriteLine($"User found: {user.Username}");

       

        // 2. Get endorsement count and recent endorsements
        var endorsementData = await _context.Endorsements
            .AsNoTracking()
            .Where(e => e.ReceiverUserId == userId)
            .OrderByDescending(e => e.EndorsementId)
            .Take(3)
            .Select(e => new
            {
                e.EndorsementId,
                e.GiverUserId,
                // Ensure Enums are converted to string safely
                SkillName = e.Skill.ToString(),
                PersonalityName = e.Personality.ToString(),
                IsPersonalityNone = e.Personality == PersonalityEndorsement.None,
                e.DateGiven
            })
            .ToListAsync();

        Console.WriteLine($"Found {endorsementData.Count} endorsements");

        var endorsements = new List<EndorsementPreviewItem>();
        foreach (var e in endorsementData)
        {
            var sender = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == e.GiverUserId);
            
            // Format comment string
            string comment = e.SkillName;
            if (!e.IsPersonalityNone)
            {
                comment += " • " + e.PersonalityName;
            }

            endorsements.Add(new EndorsementPreviewItem
            {
                EndorsementId = e.EndorsementId,
                EndorserName = sender?.Username ?? "Unknown",
                EndorserImage = sender?.ProfilePicture,
                Comment = comment,
                CreatedAt = e.DateGiven
            });
        }

        // 3. Get recent achievements (Awards)
        var achievements = await _context.Awards
            .AsNoTracking()
            .Include(a => a.Team)
                .ThenInclude(t => t!.TeamMembers)
            .Include(a => a.Schedule)
            .Where(a => a.TeamId.HasValue &&
                        a.Team!.TeamMembers.Any(tm => tm.UserId == userId))
            .OrderByDescending(a => a.AwardedDate)
            .Take(3)
            .Select(a => new AchievementItem
            {
                AwardId = a.AwardId,
                AwardName = a.AwardName,
                AwardType = a.AwardType,
                Description = a.Description,
                Position = a.Position,
                PositionName = a.Position == AwardPosition.Champion ? "Champion" :
                               a.Position == AwardPosition.FirstRunnerUp ? "1st Runner Up" : "2nd Runner Up",
                AwardedDate = a.AwardedDate,
                CompetitionTitle = a.Schedule!.GameName ?? "Competition",
                TeamName = a.Team!.TeamName ?? "Unknown Team"
            })
            .ToListAsync();

        Console.WriteLine($"Found {achievements.Count} achievements");

        // 4. Get friend count
        var friendCount = await _context.Friendships
            .AsNoTracking()
            .Where(f => (f.UserOneId == userId || f.UserTwoId == userId) &&
                        f.Status == FriendshipStatus.Accepted)
            .CountAsync();

        Console.WriteLine($"Friend count: {friendCount}");

        bool isFriend = false;
        bool hasPendingRequest = false;

        if (currentUserId.HasValue && currentUserId.Value != userId)
        {
            // Fetch the friendship row directly
            var friendship = await _context.Friendships
                .AsNoTracking()
                .FirstOrDefaultAsync(f => 
                    (f.UserOneId == currentUserId.Value && f.UserTwoId == userId) ||
                    (f.UserOneId == userId && f.UserTwoId == currentUserId.Value));

            if (friendship != null)
            {
                // DEBUG: Log the status found in DB
                Console.WriteLine($"[DEBUG] Friendship Found! ID: {friendship.FriendshipId}, Status: {friendship.Status} ({(int)friendship.Status})");

                // Check specifically for Accepted (Status 1)
                isFriend = friendship.Status == FriendshipStatus.Accepted;
                hasPendingRequest = friendship.Status == FriendshipStatus.Pending;
            }
            else
            {
                Console.WriteLine("[DEBUG] No friendship record found in DB.");
            }
        }

        // --- 3. RE-BUILD VIEWMODEL (Ensure isFriend is passed) ---
        var viewModel = new UserProfilePreviewViewModel
        {
            UserId = user.UserId,
            Username = user.Username ?? "Unknown",
            FullName = user.Username ?? "Unknown User",
            ProfileImagePath = user.ProfilePicture,
            Gender = user.Gender,
            Age = user.Age,
            Location = user.Location,
            Bio = user.Bio,
            
            // Pass the Fixed Flags
            IsFriend = isFriend,
            HasPendingFriendRequest = hasPendingRequest,
            IsCurrentUser = currentUserId.HasValue && currentUserId.Value == userId,

            
            FriendCount = await _context.Friendships.CountAsync(f => (f.UserOneId == userId || f.UserTwoId == userId) && f.Status == FriendshipStatus.Accepted),
            EndorsementCount = await _context.Endorsements.CountAsync(e => e.ReceiverUserId == userId),
            AchievementCount = await _context.Awards.CountAsync(a => a.Team != null && a.Team.TeamMembers.Any(tm => tm.UserId == userId)),
            RecentEndorsements = new List<EndorsementPreviewItem>(), 
            RecentAchievements = new List<AchievementItem>()         
        };

        return Json(new { success = true, data = viewModel });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] GetUserProfilePreview: {ex.Message}");
        return Json(new { success = false, message = "Error loading profile" });
    }
}

// Make sure GetCurrentUserId helper exists
private int? GetCurrentUserId()
{
    var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
    {
        return userId;
    }
    return null;
}
}