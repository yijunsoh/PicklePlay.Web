using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Services;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore; // Keep this if needed elsewhere
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Threading.Tasks;

namespace PicklePlay.Controllers
{
    public class CommunityController : Controller
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;


        // 5. UPDATE THE CONSTRUCTOR to inject IWebHostEnvironment
        public CommunityController(IScheduleRepository scheduleRepository, ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _scheduleRepository = scheduleRepository;
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        private int? GetCurrentUserId()
        {
            // Use GetInt32 instead of GetString
            return HttpContext.Session.GetInt32("UserId");
        }

        // Community Home Page
        // --- UPDATED: Community Home Page with community filtering ---
        public IActionResult Activity(int? communityId)
        {
            // ⬇️ ADD THIS: Get current user ID
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            // Store the current community in session or view data
            if (communityId.HasValue)
            {
                HttpContext.Session.SetInt32("CurrentCommunityId", communityId.Value);
            }
            else
            {
                // If no community specified, try to get from session
                communityId = HttpContext.Session.GetInt32("CurrentCommunityId");
            }

            // Get the current community object
            var currentCommunity = communityId.HasValue ?
                _context.Communities.Find(communityId.Value) : null;

            ViewData["CurrentCommunityId"] = communityId;
            ViewData["CurrentCommunity"] = currentCommunity;

            // --- NEW: Get additional community data ---
            if (communityId.HasValue && currentCommunity != null)
            {
                // ⬇️ ADD THIS: Get user's role in the community
                var membership = _context.CommunityMembers
                    .FirstOrDefault(cm => cm.CommunityId == communityId.Value &&
                                         cm.UserId == currentUserId.Value &&
                                         cm.Status == "Active");

                if (membership != null)
                {
                    ViewBag.UserRole = membership.CommunityRole; // Should be "Admin", "Moderator", or "Member"
                    Console.WriteLine($"User {currentUserId.Value} role in community {communityId.Value}: {membership.CommunityRole}");
                }
                else
                {
                    ViewBag.UserRole = "Guest"; // Not a member
                    Console.WriteLine($"User {currentUserId.Value} is NOT a member of community {communityId.Value}");
                }
                // ⬆️ END OF NEW CODE

                // Get member count
                var memberCount = _context.CommunityMembers
                    .Count(cm => cm.CommunityId == communityId.Value && cm.Status == "Active");
                ViewData["MemberCount"] = memberCount;

                // Get announcements (latest 5, not expired)
                var announcements = _context.CommunityAnnouncements
                    .Include(a => a.Poster)
                    .Where(a => a.CommunityId == communityId.Value &&
                               (!a.ExpiryDate.HasValue || a.ExpiryDate.Value > DateTime.UtcNow))
                    .OrderByDescending(a => a.PostDate)
                    .Take(5)
                    .ToList();
                ViewData["Announcements"] = announcements;

                // Get community members (first 15, admins first)
                var communityMembers = _context.CommunityMembers
                    .Include(cm => cm.User)
                    .Where(cm => cm.CommunityId == communityId.Value && cm.Status == "Active")
                    .OrderBy(cm => cm.CommunityRole) // Admins first
                    .ThenBy(cm => cm.User.Username)
                    .Take(15)
                    .ToList();
                ViewData["CommunityMembers"] = communityMembers;
            }
            else
            {
                // Set default values when no community is selected
                ViewBag.UserRole = "Guest"; // ⬅️ ADD THIS
                ViewData["MemberCount"] = 0;
                ViewData["Announcements"] = new List<CommunityAnnouncement>();
                ViewData["CommunityMembers"] = new List<CommunityMember>();
            }

            // Filter schedules by community
            IQueryable<Schedule> query = _context.Schedules;

            if (communityId.HasValue)
            {
                query = query.Where(s => s.CommunityId == communityId.Value);
            }

            var games = query.ToList();

            return View(games);
        }

[HttpGet]
public IActionResult CreateGameSchedule()
{
    var communityId = HttpContext.Session.GetInt32("CurrentCommunityId");
    if (communityId.HasValue)
    {
        var community = _context.Communities.Find(communityId.Value);
        ViewData["CurrentCommunity"] = community;
    }
    return View(new ScheduleUnifiedViewModel());
}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateGameSchedule(ScheduleUnifiedViewModel vm)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Json(new { success = false, message = "Please log in." });
            }

            var currentCommunityId = HttpContext.Session.GetInt32("CurrentCommunityId");

            // Validate based on schedule type
            if (vm.IsRecurring)
            {
                // Recurring validation
                if (!vm.RecurringEndDate.HasValue)
                {
                    ModelState.AddModelError("RecurringEndDate", "Please select when to stop repeating");
                }
                else if (vm.RecurringEndDate.Value < DateTime.Today)
                {
                    ModelState.AddModelError("RecurringEndDate", "End date must be in the future");
                }

                if (!vm.StartTime.HasValue)
                {
                    ModelState.AddModelError("StartTime", "Please select start time");
                }
            }
            else
            {
                // OneOff validation
                if (!vm.StartTime.HasValue)
                {
                    ModelState.AddModelError("StartTime", "Start date & time is required");
                }
                else if (vm.StartTime.Value <= DateTime.Now)
                {
                    ModelState.AddModelError("StartTime", "Please select a future date and time");
                }
            }

            // Fee validation
            if ((vm.FeeType == FeeType.PerPerson) && !vm.FeeAmount.HasValue)
            {
                ModelState.AddModelError("FeeAmount", "Fee amount is required");
            }

            // Validate RANK range
            if (vm.MinRankRestriction.HasValue && vm.MaxRankRestriction.HasValue)
            {
                if (vm.MinRankRestriction.Value > vm.MaxRankRestriction.Value)
                {
                    ModelState.AddModelError("MaxRankRestriction", "Maximum RANK must be greater than or equal to Minimum RANK");
                }
            }

            // Validate decimal places (3 decimal max)
            if (vm.MinRankRestriction.HasValue)
            {
                var minDecimalPlaces = BitConverter.GetBytes(decimal.GetBits(vm.MinRankRestriction.Value)[3])[2];
                if (minDecimalPlaces > 3)
                {
                    ModelState.AddModelError("MinRankRestriction", "Minimum RANK can have maximum 3 decimal places");
                }
            }

            if (vm.MaxRankRestriction.HasValue)
            {
                var maxDecimalPlaces = BitConverter.GetBytes(decimal.GetBits(vm.MaxRankRestriction.Value)[3])[2];
                if (maxDecimalPlaces > 3)
                {
                    ModelState.AddModelError("MaxRankRestriction", "Maximum RANK can have maximum 3 decimal places");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            try
            {
                if (vm.IsRecurring)
                {
                    // Create recurring schedule
                    await CreateRecurringScheduleFromUnified(vm, currentUserId.Value, currentCommunityId);
                }
                else
                {
                    // Create one-off schedule
                    await CreateOneOffScheduleFromUnified(vm, currentUserId.Value, currentCommunityId);
                }

                TempData["SuccessMessage"] = vm.IsRecurring
                    ? "Recurring schedule created successfully!"
                    : "Game scheduled successfully!";

                return RedirectToAction("Activity", "Community");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                return View(vm);
            }
        }

        // ⬇️ ADD: Helper method for creating one-off
        private async Task CreateOneOffScheduleFromUnified(ScheduleUnifiedViewModel vm, int userId, int? communityId)
        {
            var newSchedule = new Schedule
            {
                ScheduleType = ScheduleType.OneOff,
                GameName = vm.GameName,
                Description = vm.Description,
                EventTag = vm.EventTag,
                Location = vm.Location,
                StartTime = vm.StartTime!.Value,
                Duration = vm.Duration,
                NumPlayer = vm.NumPlayer,
                MinRankRestriction = vm.MinRankRestriction,
                MaxRankRestriction = vm.MaxRankRestriction,
                GenderRestriction = vm.GenderRestriction,
                AgeGroupRestriction = vm.AgeGroupRestriction,
                FeeType = vm.FeeType,
                FeeAmount = (vm.FeeType == FeeType.PerPerson) ? vm.FeeAmount : null,
                Privacy = vm.Privacy,
                CancellationFreeze = vm.CancellationFreeze,
                HostRole = vm.HostRole,
                CommunityId = communityId,
                CreatedByUserId = userId,
                Status = ScheduleStatus.Active
            };

            if (newSchedule.StartTime.HasValue && newSchedule.Duration.HasValue)
            {
                var durationTimeSpan = ScheduleHelper.GetTimeSpan(newSchedule.Duration.Value);
                newSchedule.EndTime = newSchedule.StartTime.Value.Add(durationTimeSpan);
            }

            _scheduleRepository.Add(newSchedule);

            // Update community's last activity date
            if (communityId.HasValue)
            {
                var community = await _context.Communities.FindAsync(communityId.Value);
                if (community != null)
                {
                    community.LastActivityDate = newSchedule.StartTime ?? DateTime.UtcNow;
                }
            }

            // Add organizer and player
            var organizer = new ScheduleParticipant
            {
                ScheduleId = newSchedule.ScheduleId,
                UserId = userId,
                Role = ParticipantRole.Organizer,
                Status = ParticipantStatus.Confirmed
            };
            _context.ScheduleParticipants.Add(organizer);

            if (newSchedule.HostRole == HostRole.HostAndPlay)
            {
                var player = new ScheduleParticipant
                {
                    ScheduleId = newSchedule.ScheduleId,
                    UserId = userId,
                    Role = ParticipantRole.Player,
                    Status = ParticipantStatus.Confirmed
                };
                _context.ScheduleParticipants.Add(player);
            }

            await _context.SaveChangesAsync();
        }

        // ⬇️ ADD: Helper method for creating recurring
        private async Task CreateRecurringScheduleFromUnified(ScheduleUnifiedViewModel vm, int userId, int? communityId)
        {
            RecurringWeek combinedWeek = RecurringWeek.None;
            foreach (var day in vm.RecurringWeek!) { combinedWeek |= day; }

            var parentSchedule = new Schedule
            {
                ScheduleType = ScheduleType.Recurring,
                ParentScheduleId = null,
                RecurringWeek = combinedWeek,
                RecurringEndDate = vm.RecurringEndDate,
                StartTime = vm.StartTime!.Value,
                GameName = vm.GameName,
                Description = vm.Description,
                EventTag = vm.EventTag,
                Location = vm.Location,
                Duration = vm.Duration,
                NumPlayer = vm.NumPlayer,
                MinRankRestriction = vm.MinRankRestriction,
                MaxRankRestriction = vm.MaxRankRestriction,
                GenderRestriction = vm.GenderRestriction,
                AgeGroupRestriction = vm.AgeGroupRestriction,
                FeeType = vm.FeeType,
                FeeAmount = (vm.FeeType == FeeType.PerPerson) ? vm.FeeAmount : null,
                Privacy = vm.Privacy,
                CancellationFreeze = vm.CancellationFreeze,
                HostRole = vm.HostRole,
                CommunityId = communityId,
                CreatedByUserId = userId,
                Status = ScheduleStatus.Active
            };

            _scheduleRepository.Add(parentSchedule);

            // Update community's last activity date
            if (communityId.HasValue)
            {
                var community = await _context.Communities.FindAsync(communityId.Value);
                if (community != null)
                {
                    community.LastActivityDate = vm.StartTime ?? DateTime.UtcNow;
                }
            }

            // Generate instances
            var durationTimeSpan = ScheduleHelper.GetTimeSpan(vm.Duration);
            var dayFlagMap = BuildDayFlagMap();

            for (var date = DateTime.Today; date <= vm.RecurringEndDate!.Value; date = date.AddDays(1))
            {
                if (dayFlagMap.TryGetValue(date.DayOfWeek, out var dayFlag) && vm.RecurringWeek.Contains(dayFlag))
                {
                    var instanceStartTime = date.Add(vm.StartTime!.Value.TimeOfDay);

                    var instanceSchedule = new Schedule
                    {
                        ScheduleType = ScheduleType.OneOff,
                        ParentScheduleId = parentSchedule.ScheduleId,
                        StartTime = instanceStartTime,
                        EndTime = instanceStartTime.Add(durationTimeSpan),
                        GameName = parentSchedule.GameName,
                        Description = parentSchedule.Description,
                        EventTag = parentSchedule.EventTag,
                        Location = parentSchedule.Location,
                        Duration = parentSchedule.Duration,
                        NumPlayer = parentSchedule.NumPlayer,
                        MinRankRestriction = parentSchedule.MinRankRestriction,
                        MaxRankRestriction = parentSchedule.MaxRankRestriction,
                        GenderRestriction = parentSchedule.GenderRestriction,
                        AgeGroupRestriction = parentSchedule.AgeGroupRestriction,
                        FeeType = parentSchedule.FeeType,
                        FeeAmount = parentSchedule.FeeAmount,
                        Privacy = parentSchedule.Privacy,
                        CancellationFreeze = parentSchedule.CancellationFreeze,
                        HostRole = parentSchedule.HostRole,
                        CommunityId = communityId,
                        CreatedByUserId = userId,
                        Status = ScheduleStatus.Active
                    };

                    _scheduleRepository.Add(instanceSchedule);

                    // Add organizer and player for each instance
                    var organizer = new ScheduleParticipant
                    {
                        ScheduleId = instanceSchedule.ScheduleId,
                        UserId = userId,
                        Role = ParticipantRole.Organizer,
                        Status = ParticipantStatus.Confirmed
                    };
                    _context.ScheduleParticipants.Add(organizer);

                    if (instanceSchedule.HostRole == HostRole.HostAndPlay)
                    {
                        var player = new ScheduleParticipant
                        {
                            ScheduleId = instanceSchedule.ScheduleId,
                            UserId = userId,
                            Role = ParticipantRole.Player,
                            Status = ParticipantStatus.Confirmed
                        };
                        _context.ScheduleParticipants.Add(player);
                    }
                }
            }

            await _context.SaveChangesAsync();
        }
        // In CommunityController.cs

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinIndividually(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized();

            var schedule = await _context.Schedules
                                 .Include(s => s.Participants)
                                 .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);
            if (schedule == null)
                return NotFound();

            var existingParticipation = schedule.Participants
                .FirstOrDefault(p => p.UserId == currentUserId && p.Role == ParticipantRole.Player);

            if (existingParticipation == null)
            {
                bool isOrganizer = schedule.Participants.Any(p => p.UserId == currentUserId.Value && p.Role == ParticipantRole.Organizer);
                bool isFree = schedule.FeeType == FeeType.Free || schedule.FeeType == FeeType.None;

                ParticipantStatus newStatus;

                // *** THIS IS THE NEW LOGIC ***
                if (schedule.RequireOrganizerApproval)
                {
                    // --- APPROVAL IS ON ---
                    // All players go to "On Hold" first.
                    newStatus = ParticipantStatus.OnHold;
                    TempData["SuccessMessage"] = "Requested! Please wait for the Organizer to Accept.";
                }
                else
                {
                    // --- APPROVAL IS OFF ---
                    // Players are sorted directly.
                    if (isFree || isOrganizer)
                    {
                        // Free games or organizers go straight to "Confirmed"
                        newStatus = ParticipantStatus.Confirmed;
                        TempData["SuccessMessage"] = "You have successfully joined this free game!";
                    }
                    else
                    {
                        // Paid games go to "Pending Payment"
                        newStatus = ParticipantStatus.PendingPayment;
                        TempData["SuccessMessage"] = "Your spot is reserved. Please complete payment.";
                    }
                }
                // *** END OF NEW LOGIC ***

                var participant = new ScheduleParticipant
                {
                    ScheduleId = scheduleId,
                    UserId = currentUserId.Value,
                    Role = ParticipantRole.Player,
                    Status = newStatus
                };
                _context.ScheduleParticipants.Add(participant);
                await _context.SaveChangesAsync();
            }
            else if (existingParticipation.Status == ParticipantStatus.Cancelled)
            {
                // Re-joining logic (unchanged)
                existingParticipation.Status = ParticipantStatus.Confirmed;
                _context.ScheduleParticipants.Update(existingParticipation);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "You have successfully re-joined the game!";
            }
            else
            {
                TempData["ErrorMessage"] = "You are already in this game.";
            }

            return RedirectToAction("Details", "Schedule", new { id = scheduleId });
        }

        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ReserveSlot(int scheduleId, int extraSlots)
{
    var currentUserId = GetCurrentUserId();
    if (!currentUserId.HasValue) return Unauthorized();

    // 1. Basic Validation: Ensure at least 1 slot is requested
    if (extraSlots < 1) extraSlots = 1;

    var schedule = await _context.Schedules
        .Include(s => s.Participants)
        .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

    if (schedule == null) return NotFound();

    var participant = schedule.Participants
        .FirstOrDefault(p => p.UserId == currentUserId && p.Role == ParticipantRole.Player);

    // 2. Server-Side Capacity Check (Prevent Overbooking)
    int maxPlayers = schedule.NumPlayer ?? 0;
    if (maxPlayers > 0)
    {
        // Count all currently occupied spots (Self + Guests)
        // We include Confirmed, PendingPayment, and OnHold to be safe
        int occupiedSlots = schedule.Participants
            .Where(p => p.Role == ParticipantRole.Player && 
                        (p.Status == ParticipantStatus.Confirmed || 
                         p.Status == ParticipantStatus.PendingPayment ||
                         p.Status == ParticipantStatus.OnHold)) 
            .Sum(p => 1 + p.ReservedSlots);

        int availableSlots = maxPlayers - occupiedSlots;

        // Calculate how many NEW spots are needed
        // If user is new: Need 1 (Self) + extraSlots
        // If user exists: Need just extraSlots
        int spotsNeeded = (participant == null) ? (1 + extraSlots) : extraSlots;

        if (spotsNeeded > availableSlots)
        {
            TempData["ErrorMessage"] = "Not enough slots available. The game filled up!";
            return RedirectToAction("Details", "Schedule", new { id = scheduleId });
        }
    }

    // 3. Create or Update Participant
    if (participant == null)
    {
        // User joining for the first time with guests
        participant = new ScheduleParticipant
        {
            ScheduleId = scheduleId,
            UserId = currentUserId.Value,
            Role = ParticipantRole.Player,
            Status = ParticipantStatus.OnHold, // Waiting for organizer approval
            ReservedSlots = extraSlots // Set guests
        };
        _context.ScheduleParticipants.Add(participant);
    }
    else
    {
        // User already joined, adding MORE guests
        participant.ReservedSlots += extraSlots; 
        
        // Optional: If they add guests, you might want to reset status to OnHold for re-approval
        participant.Status = ParticipantStatus.OnHold;
        
        _context.ScheduleParticipants.Update(participant);
    }

    await _context.SaveChangesAsync();

    TempData["SuccessMessage"] = $"Reserved {extraSlots} extra slot(s)! Organizer will review your request.";
    return RedirectToAction("Details", "Schedule", new { id = scheduleId });
}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelJoin(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
            {
                return Unauthorized(); // Not logged in
            }

            var participant = await _context.ScheduleParticipants
                .FirstOrDefaultAsync(p => p.ScheduleId == scheduleId &&
                                          p.UserId == currentUserId.Value &&
                                          p.Role == ParticipantRole.Player &&
                                          (p.Status == ParticipantStatus.OnHold ||
                                           p.Status == ParticipantStatus.PendingPayment ||
                                           p.Status == ParticipantStatus.Confirmed));

            if (participant != null)
            {
                // *** THIS IS THE NEW LOGIC FOR THE "HIDDEN" TAB ***
                if (participant.Status == ParticipantStatus.Confirmed)
                {
                    // User was CONFIRMED, so mark as "Cancelled" to show in Hidden tab
                    participant.Status = ParticipantStatus.Cancelled;
                    _context.ScheduleParticipants.Update(participant);
                    TempData["SuccessMessage"] = "You have successfully left the game. This will be moved to your 'Hidden' games list.";
                }
                else
                {
                    // User was OnHold or Pending, so just delete the record.
                    // It will NOT appear in the "Hidden" tab.
                    _context.ScheduleParticipants.Remove(participant);

                    if (participant.Status == ParticipantStatus.OnHold)
                    {
                        TempData["SuccessMessage"] = "Your request to join has been cancelled.";
                    }
                    else if (participant.Status == ParticipantStatus.PendingPayment)
                    {
                        TempData["SuccessMessage"] = "Your spot has been cancelled.";
                    }
                }

                await _context.SaveChangesAsync();
            }
            else
            {
                TempData["ErrorMessage"] = "Could not find your participation record to cancel.";
            }

            return RedirectToAction("Details", "Schedule", new { id = scheduleId });
        }

        // --- CREATE COMPETITION ---
        // GET Action: Shows the form
        [HttpGet]
        public IActionResult CreateCompetition()
        {
            return View(new ScheduleCompetitionViewModel());
        }

        // --- 
        // --- CREATE COMPETITION (POST) ---
        // --- 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCompetition(ScheduleCompetitionViewModel vm)
        {
            // --- Server-side validation ---
            if (vm.EndTime <= vm.StartTime) { ModelState.AddModelError("EndTime", "End Date & Time must be after Start Date & Time."); }
            if (vm.RegClose <= vm.RegOpen) { ModelState.AddModelError("RegClose", "Registration Close Date must be after Registration Open Date."); }
            if (vm.RegClose >= vm.StartTime) { ModelState.AddModelError("RegClose", "Registration must close before the competition starts."); }
            if (vm.EarlyBirdClose.HasValue && vm.EarlyBirdClose.Value <= vm.RegOpen) { ModelState.AddModelError("EarlyBirdClose", "Early Bird Deadline must be after Registration Open Date."); }
            if (vm.EarlyBirdClose.HasValue && vm.EarlyBirdClose.Value >= vm.RegClose) { ModelState.AddModelError("EarlyBirdClose", "Early Bird Deadline must be before Registration Close Date."); }
            if (vm.FeeType == FeeType.PerPerson && !vm.FeeAmount.HasValue) { ModelState.AddModelError("FeeAmount", "Fee Amount is required for Per Team fee type."); }
            if (vm.StartTime <= DateTime.Now) { ModelState.AddModelError("StartTime", "Competition Start Date & Time must be in the future."); }

// New: Early-bird pairing validation (both-or-none) server-side
            var earlyPriceProvided = vm.EarlyBirdPrice.HasValue && vm.EarlyBirdPrice.Value > 0m;
            var earlyCloseProvided = vm.EarlyBirdClose.HasValue;
            if (earlyPriceProvided ^ earlyCloseProvided) // XOR -> one provided but not the other
            {
                if (!earlyPriceProvided) ModelState.AddModelError("EarlyBirdPrice", "Early bird price is required when early bird deadline is set.");
                if (!earlyCloseProvided) ModelState.AddModelError("EarlyBirdClose", "Early bird deadline is required when early bird price is set.");
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }


            // --- 1. Handle File Upload FIRST ---
            string? uniqueImagePath = await ProcessUploadedImage(vm.PosterImage);

            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                return Json(new { success = false, message = "Please log in." });
            }

            var currentCommunityId = HttpContext.Session.GetInt32("CurrentCommunityId");

            // 2. Map ViewModel to Schedule Model
            var newSchedule = new Schedule
            {
                ScheduleType = ScheduleType.Competition,                
                GameName = vm.GameName,
                Description = vm.Description,
                Location = vm.Location,
                StartTime = vm.StartTime,
                EndTime = vm.EndTime,               
                RegOpen = vm.RegOpen,
                RegClose = vm.RegClose,
                EarlyBirdClose = vm.EarlyBirdClose,
                 EarlyBirdPrice = (vm.FeeType == FeeType.PerPerson) ? vm.EarlyBirdPrice : null,
                NumTeam = vm.NumTeam,
                Duration = null,
                NumPlayer = null,
                MinRankRestriction = vm.MinRankRestriction ?? 0.000m,
                MaxRankRestriction = vm.MaxRankRestriction ?? 0.000m,
                GenderRestriction = vm.GenderRestriction,
                AgeGroupRestriction = vm.AgeGroupRestriction,
                FeeType = vm.FeeType,
                FeeAmount = (vm.FeeType == FeeType.PerPerson) ? vm.FeeAmount : null,
                Privacy = vm.Privacy,
                CancellationFreeze = vm.CancellationFreeze,
                HostRole = HostRole.HostOnly,
                CommunityId = currentCommunityId,
                Status = ScheduleStatus.PendingSetup,
                CreatedByUserId = currentUserId.Value,
                CompetitionImageUrl = uniqueImagePath
            };

            // 3. Create the linked Competition entity
            var newCompetition = new Competition
            {
                NumPool = 4,
                WinnersPerPool = 1,
                ThirdPlaceMatch = true,
                DoubleRR = false,
                StandingCalculation = StandingCalculation.WinLossPoints
            };

            // 4. Link them
            newSchedule.Competition = newCompetition;

            // 5. Add the Schedule to the database
            try
            {
                _scheduleRepository.Add(newSchedule); // The schedule gets its ID here

                // Update community's last activity date
                if (currentCommunityId.HasValue)
                {
                    var community = await _context.Communities.FindAsync(currentCommunityId.Value);
                    if (community != null)
                    {
                        community.LastActivityDate = vm.StartTime;
                    }
                }

                // *** THIS IS THE FIX ***
                // Add the creator as the "Organizer" participant   
                if (currentUserId.HasValue)
                {
                    var organizer = new ScheduleParticipant
                    {
                        ScheduleId = newSchedule.ScheduleId,
                        UserId = currentUserId.Value,
                        Role = ParticipantRole.Organizer,
                        Status = ParticipantStatus.Confirmed
                    };
                    _context.ScheduleParticipants.Add(organizer);
                    await _context.SaveChangesAsync(); // Save the new organizer
                }
                // *** END OF FIX ***

                TempData["SuccessMessage"] = "Competition draft created! Proceed to setup matches.";
                return RedirectToAction("SetupMatch", new { id = newSchedule.ScheduleId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred creating the competition: {ex.Message}");
                return View(vm);
            }
        }
        // --- 
        // --- EDIT COMPETITION (GET) ---
        // --- 
        [HttpGet]
        public async Task<IActionResult> EditCompetition(int id)
        {
            var schedule = await _context.Schedules.FindAsync(id);
            if (schedule == null || schedule.ScheduleType != ScheduleType.Competition)
            {
                return NotFound();
            }

            var vm = new ScheduleCompetitionViewModel
            {
                ScheduleId = schedule.ScheduleId,
                ExistingImageUrl = schedule.CompetitionImageUrl,
                GameName = schedule.GameName!,
                Description = schedule.Description,
                Location = schedule.Location!,
                StartTime = schedule.StartTime ?? DateTime.Now, // Handle potential nulls
                EndTime = schedule.EndTime ?? DateTime.Now,               
                RegOpen = schedule.RegOpen ?? DateTime.Now,
                RegClose = schedule.RegClose ?? DateTime.Now,
                EarlyBirdClose = schedule.EarlyBirdClose,
                EarlyBirdPrice = (schedule.FeeType == FeeType.PerPerson) ? schedule.EarlyBirdPrice : null,
                NumTeam = schedule.NumTeam ?? 8,
                MinRankRestriction = schedule.MinRankRestriction ?? 0.000m,
                MaxRankRestriction = schedule.MaxRankRestriction ?? 0.000m,
                GenderRestriction = schedule.GenderRestriction ?? Models.GenderRestriction.None,
                AgeGroupRestriction = schedule.AgeGroupRestriction ?? Models.AgeGroupRestriction.Adult,
                FeeType = schedule.FeeType ?? Models.FeeType.Free,
                FeeAmount = schedule.FeeAmount,
                Privacy = schedule.Privacy ?? Models.Privacy.Public,

                // --- THIS IS THE FIX for CS0117 ---
                CancellationFreeze = schedule.CancellationFreeze ?? Models.CancellationFreeze.None,
            };

            // "CreateCompetition" is the correct view name, as it's our shared edit/create form
            return View("CreateCompetition", vm);
        }

        // --- 
        // --- EDIT COMPETITION (POST) ---
        // --- 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCompetition(int id, ScheduleCompetitionViewModel vm)
        {
            // tolerate missing binding of ScheduleId from the form: use route id when vm.ScheduleId == 0
            if (vm.ScheduleId == 0) vm.ScheduleId = id;
            if (id != vm.ScheduleId) return BadRequest();

            // Re-run validation
            if (vm.EndTime <= vm.StartTime) { ModelState.AddModelError("EndTime", "End Date & Time must be after Start Date & Time."); }
            if (vm.RegClose <= vm.RegOpen) { ModelState.AddModelError("RegClose", "Registration Close Date must be after Registration Open Date."); }
            if (vm.RegClose >= vm.StartTime) { ModelState.AddModelError("RegClose", "Registration must close before the competition starts."); }
            // ... (add other validation checks as needed) ...

            if (!ModelState.IsValid)
            {
                // Must re-populate ExistingImageUrl if validation fails
                vm.ExistingImageUrl = vm.ExistingImageUrl; // It was posted back in a hidden field
                return View("CreateCompetition", vm);
            }

            var scheduleToUpdate = await _context.Schedules.FindAsync(id);
            if (scheduleToUpdate == null) return NotFound();

            // Handle file upload
            if (vm.PosterImage != null)
            {
                // Delete old image if it exists
                if (!string.IsNullOrEmpty(scheduleToUpdate.CompetitionImageUrl))
                {
                    var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, scheduleToUpdate.CompetitionImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }
                // Save new image
                scheduleToUpdate.CompetitionImageUrl = await ProcessUploadedImage(vm.PosterImage);
            }

            // Map all other properties
            scheduleToUpdate.GameName = vm.GameName;
            scheduleToUpdate.Description = vm.Description;
            scheduleToUpdate.Location = vm.Location;
            scheduleToUpdate.StartTime = vm.StartTime;
            scheduleToUpdate.EndTime = vm.EndTime;           
            scheduleToUpdate.RegOpen = vm.RegOpen;
            scheduleToUpdate.RegClose = vm.RegClose;
            scheduleToUpdate.EarlyBirdClose = vm.EarlyBirdClose;
            scheduleToUpdate.NumTeam = vm.NumTeam;
            scheduleToUpdate.MinRankRestriction = vm.MinRankRestriction;
            scheduleToUpdate.MaxRankRestriction = vm.MaxRankRestriction;
            scheduleToUpdate.GenderRestriction = vm.GenderRestriction;
            scheduleToUpdate.AgeGroupRestriction = vm.AgeGroupRestriction;
            scheduleToUpdate.FeeType = vm.FeeType;
            scheduleToUpdate.FeeAmount = (vm.FeeType == FeeType.PerPerson) ? vm.FeeAmount : null;
            scheduleToUpdate.Privacy = vm.Privacy;
            scheduleToUpdate.CancellationFreeze = vm.CancellationFreeze;

            try
            {
                _context.Update(scheduleToUpdate);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Competition details updated successfully!";
                return RedirectToAction("CompDetails", "Competition", new { id = scheduleToUpdate.ScheduleId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred updating the competition: {ex.Message}");
                vm.ExistingImageUrl = scheduleToUpdate.CompetitionImageUrl; // Pass back current image
                return View("CreateCompetition", vm);
            }
        }


        // GET: /Community/SetupMatch/{id}
        [HttpGet]
        public IActionResult SetupMatch(int id)
        {
            var schedule = _context.Schedules
                                   .Include(s => s.Competition)
                                   .FirstOrDefault(s => s.ScheduleId == id);

            if (schedule == null) { return NotFound("Schedule not found."); }
            if (schedule.ScheduleType != ScheduleType.Competition) { return BadRequest("This schedule is not a competition."); }
            if (schedule.Competition == null) { return NotFound("Competition details missing for this schedule."); }

            var viewModel = new CompetitionSetupViewModel
            {
                ScheduleId = schedule.ScheduleId,
                GameName = schedule.GameName,
                Format = schedule.Competition.Format,
                NumPool = schedule.Competition.NumPool,
                WinnersPerPool = schedule.Competition.WinnersPerPool,
                StandingCalculation = schedule.Competition.StandingCalculation,
                StandardWin = schedule.Competition.StandardWin,
                StandardLoss = schedule.Competition.StandardLoss,
                TieBreakWin = schedule.Competition.TieBreakWin,
                TieBreakLoss = schedule.Competition.TieBreakLoss,
                Draw = schedule.Competition.Draw,
                ThirdPlaceMatch = schedule.Competition.ThirdPlaceMatch,
                DoubleRR = schedule.Competition.DoubleRR,
                MatchRule = schedule.Competition.MatchRule
            };

            return View(viewModel);
        }

        // POST: /Community/SetupMatch/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetupMatch(int id, CompetitionSetupViewModel vm)
        {
            if (id != vm.ScheduleId) { return BadRequest("ID mismatch."); }
            // tolerate missing/incorrect binding from the form:
            // - if vm.ScheduleId is 0 (not bound), use route id
            // - if both present but different, treat as mismatch
            if (vm.ScheduleId == 0) vm.ScheduleId = id;
           if (id != vm.ScheduleId) { return BadRequest("ID mismatch."); }

            if (vm.Format == CompetitionFormat.PoolPlay)
            {
                if (vm.NumPool <= 0) { ModelState.AddModelError("NumPool", "Number of pools must be positive."); }
                if (vm.WinnersPerPool <= 0) { ModelState.AddModelError("WinnersPerPool", "Winners per pool must be positive."); }
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var scheduleToUpdate = _context.Schedules
                                         .Include(s => s.Competition)
                                         .FirstOrDefault(s => s.ScheduleId == id);

            if (scheduleToUpdate == null || scheduleToUpdate.Competition == null)
            {
                return NotFound("Competition or schedule not found for update.");
            }

            scheduleToUpdate.Competition.Format = vm.Format;
            scheduleToUpdate.Competition.MatchRule = vm.MatchRule;

            // --- NEW LOGIC ---
            // Save settings based on the selected format
            // We save ALL values, and the UI can just hide/show what's relevant.

            // Pool Play Settings
            scheduleToUpdate.Competition.NumPool = vm.NumPool;
            scheduleToUpdate.Competition.WinnersPerPool = vm.WinnersPerPool;
            scheduleToUpdate.Competition.StandingCalculation = vm.StandingCalculation;
            scheduleToUpdate.Competition.StandardWin = vm.StandardWin;
            scheduleToUpdate.Competition.StandardLoss = vm.StandardLoss;
            scheduleToUpdate.Competition.TieBreakWin = vm.TieBreakWin;
            scheduleToUpdate.Competition.TieBreakLoss = vm.TieBreakLoss;
            scheduleToUpdate.Competition.Draw = vm.Draw;

            // Elimination Settings
            scheduleToUpdate.Competition.ThirdPlaceMatch = vm.ThirdPlaceMatch; // <-- THIS IS THE FIX

            // Round Robin Settings
            scheduleToUpdate.Competition.DoubleRR = vm.DoubleRR;
            // --- END OF NEW LOGIC ---

            scheduleToUpdate.Status = ScheduleStatus.Active;

            try
            {
                _context.SaveChanges();

                TempData["SuccessMessage"] = "Competition setup saved successfully!";

                // --- THIS IS THE FIX ---
                // Changed "Details", "Schedule" to "CompDetails", "Competition"
                return RedirectToAction("CompDetails", "Competition", new { id = scheduleToUpdate.ScheduleId });
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError("", "The record you attempted to edit was modified by another user. Please reload and try again.");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred saving the setup: {ex.Message}");
            }

            return View(vm);
        }

        // ------------------------

        // POST: /Community/Publish/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Publish(int id)
        {
            var scheduleToPublish = _context.Schedules
                                          .Include(s => s.Competition)
                                          .FirstOrDefault(s => s.ScheduleId == id);

            if (scheduleToPublish == null)
            {
                TempData["ErrorMessage"] = "Schedule not found.";
                return RedirectToAction("Activity");
            }

            if (scheduleToPublish.ScheduleType != ScheduleType.Competition || scheduleToPublish.Competition == null)
            {
                TempData["ErrorMessage"] = "This schedule is not a competition or is missing details.";
                return RedirectToAction("Activity");
            }

            if (scheduleToPublish.Status != ScheduleStatus.PendingSetup)
            {
                TempData["ErrorMessage"] = "This competition cannot be published because its status is not 'Pending Setup'.";
                return RedirectToAction("Activity");
            }

            scheduleToPublish.Status = ScheduleStatus.Active;

            try
            {
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Competition '{scheduleToPublish.GameName}' published successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error publishing competition: {ex.Message}";
            }

            return RedirectToAction("Listing", "Competition");
        }

        // --- 
        // --- NEW: Helper method for processing images (FIX for CS0103) ---
        // --- 
        private async Task<string?> ProcessUploadedImage(IFormFile? posterImage)
        {
            if (posterImage == null || posterImage.Length == 0) return null;

            string uniqueImagePath;
            // Path relative to wwwroot
            string uploadsFolderRelative = "img/posters";
            // Full path to save the file
            string uploadsFolderAbsolute = Path.Combine(_webHostEnvironment.WebRootPath, uploadsFolderRelative);

            Directory.CreateDirectory(uploadsFolderAbsolute); // Ensures the folder exists

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + posterImage.FileName;
            string filePath = Path.Combine(uploadsFolderAbsolute, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await posterImage.CopyToAsync(fileStream);
            }

            // Store the relative path (with forward slashes for web)
            uniqueImagePath = $"/{uploadsFolderRelative}/{uniqueFileName}";
            return uniqueImagePath;
        }

        // --- HELPER METHODS FOR RECURRING ---
        private RecurringWeek GetTodayDayFlag()
        {
            switch (DateTime.Today.DayOfWeek)
            {
                case DayOfWeek.Monday: return RecurringWeek.Mon;
                case DayOfWeek.Tuesday: return RecurringWeek.Tue;
                case DayOfWeek.Wednesday: return RecurringWeek.Wed;
                case DayOfWeek.Thursday: return RecurringWeek.Thur;
                case DayOfWeek.Friday: return RecurringWeek.Fri;
                case DayOfWeek.Saturday: return RecurringWeek.Sat;
                case DayOfWeek.Sunday: return RecurringWeek.Sun;
                default: return RecurringWeek.None;
            }
        }
        private Dictionary<DayOfWeek, RecurringWeek> BuildDayFlagMap()
        {
            return new Dictionary<DayOfWeek, RecurringWeek>
            {
                [DayOfWeek.Monday] = RecurringWeek.Mon,
                [DayOfWeek.Tuesday] = RecurringWeek.Tue,
                [DayOfWeek.Wednesday] = RecurringWeek.Wed,
                [DayOfWeek.Thursday] = RecurringWeek.Thur,
                [DayOfWeek.Friday] = RecurringWeek.Fri,
                [DayOfWeek.Saturday] = RecurringWeek.Sat,
                [DayOfWeek.Sunday] = RecurringWeek.Sun
            };
        }

        // --- ACTION 1: Search for users to add as organizer ---
        [HttpGet]
        public async Task<IActionResult> SearchUsersForOrganizer(int scheduleId, string query)
        {
            var currentUserId = GetCurrentUserId();
            if (string.IsNullOrEmpty(query) || query.Length < 2)
            {
                return Json(new List<object>());
            }

            var users = await _context.Users
                .Where(u => u.Username.Contains(query) && u.UserId != currentUserId)
                .Take(10)
                .ToListAsync();

            var existingParticipantUserIds = await _context.ScheduleParticipants
                .Where(p => p.ScheduleId == scheduleId)
                .Select(p => p.UserId)
                .ToListAsync();

            var results = users
                .Where(u => !existingParticipantUserIds.Contains(u.UserId))
                .Select(u => new
                {
                    userId = u.UserId,
                    username = u.Username,
                    profilePicture = u.ProfilePicture
                });

            return Json(results);
        }

        // --- ACTION 2: Add the user as an organizer ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddOrganizer(int scheduleId, int userId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized();

            bool isOrganizer = await _context.ScheduleParticipants
                .AnyAsync(p => p.ScheduleId == scheduleId &&
                               p.UserId == currentUserId.Value &&
                               p.Role == ParticipantRole.Organizer);

            if (!isOrganizer)
                return Forbid(); // Only organizers can add other organizers

            bool alreadyExists = await _context.ScheduleParticipants
                .AnyAsync(p => p.ScheduleId == scheduleId && p.UserId == userId);

            if (alreadyExists)
            {
                return BadRequest(new { message = "This user is already a participant in this game." });
            }

            var newOrganizer = new ScheduleParticipant
            {
                ScheduleId = scheduleId,
                UserId = userId,
                Role = ParticipantRole.Organizer,
                Status = ParticipantStatus.Confirmed // Organizers are auto-confirmed
            };

            _context.ScheduleParticipants.Add(newOrganizer);
            await _context.SaveChangesAsync();

            return Json(new { message = "Organizer added successfully." });
        }

        public IActionResult EnterCommunity(int communityId)
        {
            // Store community in session
            HttpContext.Session.SetInt32("CurrentCommunityId", communityId);

            // Redirect to activity page with the community context
            return RedirectToAction("Activity", new { communityId = communityId });
        }
    }
}