using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Linq; // Needed for Select
using Microsoft.EntityFrameworkCore; // *** ADD THIS ***
using System.Threading.Tasks; // *** ADD THIS ***
using Microsoft.AspNetCore.Hosting; // *** ADD THIS ***
using PicklePlay.Services; // *** ADD THIS ***
using PicklePlay.Helpers; // *** ADD THIS ***

namespace PicklePlay.Controllers
{
    public class ScheduleController : Controller
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly ApplicationDbContext _context; // Add this
        private readonly IAuthService _authService; // Add this
        private readonly IWebHostEnvironment _environment; // Add this

        public ScheduleController(IScheduleRepository scheduleRepository,
                              ApplicationDbContext context, // Add this
                              IAuthService authService, // Add this
                              IWebHostEnvironment environment) // Add this
        {
            _scheduleRepository = scheduleRepository;
            _context = context; // Add this
            _authService = authService; // Add this
            _environment = environment; // Add this
        }
        


        // *** MODIFIED THIS ACTION ***
public async Task<IActionResult> GameListing()
{
    var schedules = await _context.Schedules
        .Include(s => s.Participants) // Needed for counting players
        .Include(s => s.Community)    // ⬅️ CRITICAL: Needed to show Community Name & Icon
        .Where(s => 
            s.ScheduleType == ScheduleType.OneOff &&   // Only show One-Off games
            s.StartTime >= DateTime.Today &&           // Only show future games
            s.Privacy == Privacy.Public &&             // Only Public games
            (s.Community == null || s.Community.CommunityType == "Public") // Only Public Communities (or no community)
        )
        .OrderBy(s => s.StartTime)
        .ToListAsync();

    return View(schedules);
}
        private int? GetCurrentUserId()
        {
            // Use GetInt32 instead of GetString
            return HttpContext.Session.GetInt32("UserId");
        }

        // *** ALSO MODIFIED THIS ACTION (to fix the Details page) ***
        public async Task<IActionResult> Details(int id)
        {
            // You were missing the .Include(p => p.User) here
            var schedule = await _context.Schedules
                                         .Include(s => s.Participants)
                                             .ThenInclude(p => p.User)
                                         .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // --- FIX: This logic sets the ViewBag variable ---
            var currentUserId = GetCurrentUserId();
            bool isBookmarked = false;
            if (currentUserId.HasValue)
            {
                // Check the database to see if a bookmark exists
                isBookmarked = await _context.Bookmarks
                    .AnyAsync(b => b.ScheduleId == id && b.UserId == currentUserId.Value);
            }

            // This passes the true/false value to the Details.cshtml view
            ViewBag.IsBookmarked = isBookmarked;
            // --- END OF FIX ---

            // --- Set Escrow Status ---
            var escrow = await _context.Escrows
                .FirstOrDefaultAsync(e => e.ScheduleId == id);
            
            if (escrow != null)
            {
                ViewData["EscrowStatus"] = escrow.Status;
            }
            // --- END: Set Escrow Status ---

            return View(schedule);
        }

        public IActionResult MyGames()
        {
            // Placeholder: Replace with logic to get games for the current user
            var myGames = _scheduleRepository.All().Take(2); // Example
            return View(myGames);
        }

        // --- EDIT ACTIONS ---

        // GET: /Schedule/Edit/{id}
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var schedule = _scheduleRepository.GetById(id);
            if (schedule == null)
            {
                return NotFound();
            }

            // Map Schedule model to Edit ViewModel
            var viewModel = new ScheduleEditViewModel
            {
                ScheduleId = schedule.ScheduleId,
                ScheduleType = schedule.ScheduleType ?? Models.ScheduleType.OneOff, // Default if null
                GameName = schedule.GameName ?? "",
                Description = schedule.Description,
                EventTag = schedule.EventTag ?? Models.EventTag.None,
                Location = schedule.Location ?? "",
                Duration = schedule.Duration ?? Models.Duration.H2,
                NumPlayer = schedule.NumPlayer ?? 8,
                Privacy = schedule.Privacy ?? Models.Privacy.Public,
                FeeType = schedule.FeeType ?? Models.FeeType.PerPerson,
                FeeAmount = schedule.FeeAmount,
                MinRankRestriction = schedule.MinRankRestriction,
                MaxRankRestriction = schedule.MaxRankRestriction,
                GenderRestriction = schedule.GenderRestriction ?? Models.GenderRestriction.None,
                AgeGroupRestriction = schedule.AgeGroupRestriction ?? Models.AgeGroupRestriction.Adult,
                CancellationFreeze = schedule.CancellationFreeze ?? Models.CancellationFreeze.None,
                HostRole = schedule.HostRole ?? Models.HostRole.HostAndPlay,

                RequireOrganizerApproval = schedule.RequireOrganizerApproval,


                // One-Off specific
                StartTime = schedule.StartTime, // Keep as DateTime?


                // Recurring specific
                RecurringWeek = new List<RecurringWeek>(), // Initialize
                RecurringStartTime = schedule.StartTime.HasValue ? TimeOnly.FromDateTime(schedule.StartTime.Value) : null,               
            };

            // Deconstruct RecurringWeek flags
            if (schedule.RecurringWeek.HasValue && schedule.RecurringWeek != RecurringWeek.None)
            {
                foreach (RecurringWeek day in Enum.GetValues(typeof(RecurringWeek)))
                {
                    if (day != RecurringWeek.None && schedule.RecurringWeek.Value.HasFlag(day))
                    {
                        viewModel.RecurringWeek.Add(day);
                    }
                }
            }


            // Choose the correct view based on ScheduleType
            if (viewModel.ScheduleType == ScheduleType.Recurring)
            {
                return View("EditRecurring", viewModel);
            }
            else // Default to OneOff
            {
                return View("EditOneOff", viewModel);
            }
        }

        // POST: /Schedule/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, ScheduleEditViewModel vm)
        {
            if (id != vm.ScheduleId)
            {
                return BadRequest("ID mismatch.");
            }

            // --- Server-side validation block (Keep as is from previous step) ---
            if (vm.ScheduleType == ScheduleType.OneOff)
            {
                // Check if StartTime is provided
                if (!vm.StartTime.HasValue)
                {
                    ModelState.AddModelError("StartTime", "Start Date & Time is required for One-Off schedules.");
                }
                // Check if StartTime is in the past (only if it has a value)
                else if (vm.StartTime.Value <= DateTime.Now)
                {
                    ModelState.AddModelError("StartTime", "Please select a future date and time.");
                }
            }
            else if (vm.ScheduleType == ScheduleType.Recurring)
            {
                // Check if RecurringStartTime is provided
                if (!vm.RecurringStartTime.HasValue)
                {
                    ModelState.AddModelError("RecurringStartTime", "Start Time is required for Recurring schedules.");
                }
                else
                {
                    // Check future time if 'Today' is selected
                    RecurringWeek todayDayFlag = RecurringWeek.None;
                    // ... (switch logic to get todayDayFlag remains the same) ...
                    switch (DateTime.Today.DayOfWeek)
                    {
                        case DayOfWeek.Monday: todayDayFlag = RecurringWeek.Mon; break;
                        case DayOfWeek.Tuesday: todayDayFlag = RecurringWeek.Tue; break;
                        case DayOfWeek.Wednesday: todayDayFlag = RecurringWeek.Wed; break;
                        case DayOfWeek.Thursday: todayDayFlag = RecurringWeek.Thur; break;
                        case DayOfWeek.Friday: todayDayFlag = RecurringWeek.Fri; break;
                        case DayOfWeek.Saturday: todayDayFlag = RecurringWeek.Sat; break;
                        case DayOfWeek.Sunday: todayDayFlag = RecurringWeek.Sun; break;
                    }

                    if (vm.RecurringWeek != null && vm.RecurringWeek.Contains(todayDayFlag))
                    {
                        var selectedDateTimeToday = DateTime.Today.Add(vm.RecurringStartTime.Value.ToTimeSpan());
                        if (selectedDateTimeToday <= DateTime.Now)
                        {
                            ModelState.AddModelError("RecurringStartTime", "Please select a future time if recurring on today's date.");
                        }
                    }
                }
                // Check if recurring days are selected
                if (vm.RecurringWeek == null || !vm.RecurringWeek.Any())
                {
                    ModelState.AddModelError("RecurringWeek", "Please select at least one day for recurring schedules.");
                }
            }
            // --- END VALIDATION ---


            if (!ModelState.IsValid)
            {
                if (vm.ScheduleType == ScheduleType.Recurring)
                {
                    return View("EditRecurring", vm);
                }
                else
                {
                    return View("EditOneOff", vm);
                }
            }

            // --- Retrieve and Update Logic ---
            var scheduleToUpdate = _scheduleRepository.GetById(id);
            if (scheduleToUpdate == null)
            {
                return NotFound();
            }

            // ... (Mapping logic remains the same) ...
            scheduleToUpdate.GameName = vm.GameName;
            scheduleToUpdate.Description = vm.Description;
            scheduleToUpdate.EventTag = vm.EventTag;
            scheduleToUpdate.Location = vm.Location;
            scheduleToUpdate.Duration = vm.Duration;
            scheduleToUpdate.NumPlayer = vm.NumPlayer;
            scheduleToUpdate.Privacy = vm.Privacy;
            scheduleToUpdate.FeeType = vm.FeeType;
            scheduleToUpdate.FeeAmount = (vm.FeeType == FeeType.PerPerson) ? vm.FeeAmount : null;
            scheduleToUpdate.MinRankRestriction = vm.MinRankRestriction;
            scheduleToUpdate.MaxRankRestriction = vm.MaxRankRestriction;
            scheduleToUpdate.GenderRestriction = vm.GenderRestriction;
            scheduleToUpdate.AgeGroupRestriction = vm.AgeGroupRestriction;
            scheduleToUpdate.CancellationFreeze = vm.CancellationFreeze;
            scheduleToUpdate.HostRole = vm.HostRole;

            // --- ADD THIS LINE ---
            scheduleToUpdate.RequireOrganizerApproval = vm.RequireOrganizerApproval;



            // Update type-specific fields
            if (vm.ScheduleType == ScheduleType.Recurring)
            {
                if (vm.RecurringStartTime.HasValue)
                {
                    scheduleToUpdate.StartTime = DateTime.Today.Add(vm.RecurringStartTime.Value.ToTimeSpan());
                }
                else
                {
                    scheduleToUpdate.StartTime = null; // Should be caught by validation
                }               
                RecurringWeek combinedWeek = RecurringWeek.None;
                if (vm.RecurringWeek != null && vm.RecurringWeek.Count > 0) // Already checked for null/empty
                {
                    foreach (var day in vm.RecurringWeek) { combinedWeek |= day; }
                }
                scheduleToUpdate.RecurringWeek = combinedWeek;

            }
            else // OneOff
            {
                scheduleToUpdate.StartTime = vm.StartTime; // Already checked for null

                scheduleToUpdate.RecurringWeek = null;                
            }

            // --- Recalculate EndTime ---
            if (scheduleToUpdate.StartTime.HasValue && scheduleToUpdate.Duration.HasValue)
            {
                var durationTimeSpan = ScheduleHelper.GetTimeSpan(scheduleToUpdate.Duration.Value);
                scheduleToUpdate.EndTime = scheduleToUpdate.StartTime.Value.Add(durationTimeSpan);
            }
            else
            {
                scheduleToUpdate.EndTime = null;
            }


            try
            {
                _scheduleRepository.Update(scheduleToUpdate);
                TempData["SuccessMessage"] = "Schedule updated successfully!";
                return RedirectToAction("Details", new { id = scheduleToUpdate.ScheduleId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred updating the schedule: {ex.Message}");
                if (vm.ScheduleType == ScheduleType.Recurring)
                {
                    return View("EditRecurring", vm);
                }
                else
                {
                    return View("EditOneOff", vm);
                }
            }
        }
        // --- END EDIT ACTIONS ---

        // --- DELETE ACTIONS ---

        // GET: /Schedule/Delete/{id} - Shows confirmation page
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var schedule = _scheduleRepository.GetById(id);
            if (schedule == null)
            {
                return NotFound();
            }
            return View(schedule); // Pass the schedule to the view for confirmation details
        }

        // POST: /Schedule/Delete/{id} - Performs the delete
        [HttpPost, ActionName("Delete")] // Use ActionName to map POST to /Schedule/Delete/{id}
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var schedule = _scheduleRepository.GetById(id);
            if (schedule == null)
            {
                // Should not happen if GET worked, but good practice
                return NotFound();
            }

            try
            {
                _scheduleRepository.Delete(id);
                // Optional: Add TempData success message
                TempData["SuccessMessage"] = "Schedule deleted successfully!";
                return RedirectToAction("Activity", "Community"); // Redirect to listing after delete
            }
            catch (Exception ex)
            {
                // Log the error
                // Add error message to TempData or ModelState
                TempData["ErrorMessage"] = $"Error deleting schedule: {ex.Message}";
                // Redirect back to details or show error view
                return RedirectToAction("Details", new { id = id });
            }
        }
        // --- END DELETE ACTIONS ---

        // --- NEW ACTION: EndGame ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndGame(int scheduleId)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            // Check if user is an organizer for this game
            var isOrganizer = await _context.ScheduleParticipants
                .AnyAsync(p => p.ScheduleId == scheduleId &&
                               p.UserId == currentUserId.Value &&
                               p.Role == ParticipantRole.Organizer);

            if (!isOrganizer) return Forbid();

            var schedule = await _context.Schedules.FindAsync(scheduleId);
            if (schedule == null) return NotFound();

            // Set the status to allow endorsements
            schedule.EndorsementStatus = EndorsementStatus.PendingEndorsement;
            schedule.Status = ScheduleStatus.Past; // Also mark the game as Past
            _context.Schedules.Update(schedule);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Game has been ended! Participants can now leave endorsements.";
            return RedirectToAction("Details", new { id = scheduleId });
        }

        // Add these methods to your ScheduleController:

        // GET: /Schedule/GetScheduleChatHistory
        [HttpGet]
        public async Task<IActionResult> GetScheduleChatHistory(int scheduleId, int skip = 0, int take = 50)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return NotFound(new { success = false, message = "Schedule not found." });
                }

                // ⬇️ FIXED: Check if user is SCHEDULE ORGANIZER
                var isScheduleOrganizer = await _context.ScheduleParticipants
                    .AnyAsync(sp => sp.ScheduleId == scheduleId &&
                                   sp.UserId == userId &&
                                   sp.Role == ParticipantRole.Organizer);

                // Check if user is in a confirmed team
                var isInConfirmedTeam = await _context.Teams
                    .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
                    .AnyAsync(team => team.TeamMembers.Any(tm =>
                        tm.UserId == userId &&
                        tm.Status == TeamMemberStatus.Joined
                    ));

                // User must be organizer OR in confirmed team
                if (!isScheduleOrganizer && !isInConfirmedTeam)
                {
                    return Forbid("Only organizers and confirmed team members can view schedule chat.");
                }

                // Get chat history
                var messages = await _context.ScheduleChatMessages
                    .Include(m => m.Sender)
                    .Where(m => m.ScheduleId == scheduleId && !m.IsDeleted)
                    .OrderByDescending(m => m.SentAt)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync();

                // Map messages and check organizer status for each sender
                var mappedMessages = new List<object>();
                foreach (var m in messages)
                {
                    // ⬇️ FIXED: Check if sender is SCHEDULE ORGANIZER (not team captain)
                    var senderIsOrganizer = await _context.ScheduleParticipants
                        .AnyAsync(sp => sp.ScheduleId == scheduleId &&
                                       sp.UserId == m.SenderId &&
                                       sp.Role == ParticipantRole.Organizer);

                    mappedMessages.Add(new
                    {
                        messageId = m.MessageId,
                        scheduleId = m.ScheduleId,
                        senderId = m.SenderId,
                        senderName = m.Sender?.Username ?? "Unknown",
                        senderProfilePicture = m.Sender?.ProfilePicture,
                        content = m.Content,
                        sentAt = m.SentAt,
                        isOrganizer = senderIsOrganizer, // ⬅️ FIXED
                        isMine = m.SenderId == userId
                    });
                }

                // Reverse to show oldest to newest
                mappedMessages.Reverse();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        messages = mappedMessages,
                        hasMore = messages.Count == take
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading schedule chat history: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while loading chat history."
                });
            }
        }

        // Update DeleteScheduleMessage:
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteScheduleMessage(int messageId, int scheduleId)
        {
            var currentUserIdInt = HttpContext.Session.GetInt32("UserId");
            if (!currentUserIdInt.HasValue || currentUserIdInt.Value <= 0)
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            int userId = currentUserIdInt.Value;

            try
            {
                var message = await _context.ScheduleChatMessages
                    .FirstOrDefaultAsync(m => m.MessageId == messageId && m.ScheduleId == scheduleId);

                if (message == null)
                {
                    return NotFound(new { success = false, message = "Message not found." });
                }

                // ⬇️ FIXED: Check if user is SCHEDULE ORGANIZER (not team captain)
                var isScheduleOrganizer = await _context.ScheduleParticipants
                    .AnyAsync(sp => sp.ScheduleId == scheduleId &&
                                   sp.UserId == userId &&
                                   sp.Role == ParticipantRole.Organizer);

                bool isOwner = message.SenderId == userId;
                bool canDelete = isScheduleOrganizer || isOwner;

                if (!canDelete)
                {
                    return Forbid("You don't have permission to delete this message.");
                }

                // Soft delete
                message.IsDeleted = true;
                message.DeletedAt = DateTime.UtcNow;
                message.DeletedByUserId = userId;

                _context.ScheduleChatMessages.Update(message);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Schedule message {messageId} deleted by user {userId} (Schedule Organizer: {isScheduleOrganizer})");

                return Ok(new
                {
                    success = true,
                    message = "Message deleted successfully.",
                    messageId = messageId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting schedule message: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while deleting the message."
                });
            }
        }

        [HttpGet]
public async Task<IActionResult> TestAutoEnd(int hoursThreshold = 24)
{
    // Only allow in development
    if (!_environment.IsDevelopment())
    {
        return NotFound();
    }

    var nowUtc = DateTime.UtcNow;
    var nowMalaysia = DateTimeHelper.GetMalaysiaTime();

    var result = new
    {
        currentTimeUTC = nowUtc.ToString("yyyy-MM-dd HH:mm:ss"),
        currentTimeMalaysia = nowMalaysia.ToString("yyyy-MM-dd HH:mm:ss"),
        autoEndThreshold = nowUtc.AddHours(-hoursThreshold).ToString("yyyy-MM-dd HH:mm:ss"),
        thresholdHours = hoursThreshold,
        schedules = new List<object>(),
        schedulesChanged = new List<int>()
    };

    // Check Competitions: InProgress (7) → Completed (8)
    var competitions = await _context.Schedules
        .Where(s => s.ScheduleType == ScheduleType.Competition &&
                   s.Status == ScheduleStatus.InProgress && // Status = 7
                   s.EndTime.HasValue)
        .ToListAsync();

    foreach (var comp in competitions)
    {
        var endTime = DateTime.SpecifyKind(comp.EndTime!.Value, DateTimeKind.Utc);
        var timeSinceEnd = nowUtc - endTime;
        var shouldEnd = timeSinceEnd.TotalHours >= hoursThreshold;

        ((List<object>)result.schedules).Add(new
        {
            scheduleId = comp.ScheduleId,
            type = "Competition",
            name = comp.GameName,
            statusBefore = "InProgress",
            statusBeforeInt = 7,
            endTimeUTC = endTime.ToString("yyyy-MM-dd HH:mm:ss"),
            hoursSinceEnd = Math.Round(timeSinceEnd.TotalHours, 2),
            shouldAutoEnd = shouldEnd,
            statusAfter = shouldEnd ? "Completed" : "InProgress",
            statusAfterInt = shouldEnd ? 8 : 7
        });

        if (shouldEnd)
        {
            comp.Status = ScheduleStatus.Completed; // Status = 8
            ((List<int>)result.schedulesChanged).Add(comp.ScheduleId);
        }
    }

    // Check Game Schedules: Active (1) → Past (2)
    var oneOffGames = await _context.Schedules
        .Where(s => s.ScheduleType == ScheduleType.OneOff &&
                   s.Status == ScheduleStatus.Active && // Status = 1
                   s.StartTime.HasValue &&
                   s.Duration.HasValue)
        .ToListAsync();

    foreach (var game in oneOffGames)
    {
        var startTime = DateTime.SpecifyKind(game.StartTime!.Value, DateTimeKind.Utc);
        var durationTimeSpan = ScheduleHelper.GetTimeSpan(game.Duration!.Value);
        var calculatedEndTime = startTime.Add(durationTimeSpan);
        var timeSinceEnd = nowUtc - calculatedEndTime;
        var shouldEnd = timeSinceEnd.TotalHours >= hoursThreshold;

        ((List<object>)result.schedules).Add(new
        {
            scheduleId = game.ScheduleId,
            type = "OneOff Game",
            name = game.GameName,
            statusBefore = "Active",
            statusBeforeInt = 1,
            startTimeUTC = startTime.ToString("yyyy-MM-dd HH:mm:ss"),
            duration = game.Duration.ToString(),
            calculatedEndTimeUTC = calculatedEndTime.ToString("yyyy-MM-dd HH:mm:ss"),
            hoursSinceEnd = Math.Round(timeSinceEnd.TotalHours, 2),
            shouldAutoEnd = shouldEnd,
            statusAfter = shouldEnd ? "Past" : "Active",
            statusAfterInt = shouldEnd ? 2 : 1
        });

        if (shouldEnd)
        {
            game.Status = ScheduleStatus.Past; // Status = 2
            ((List<int>)result.schedulesChanged).Add(game.ScheduleId);
        }
    }

    // Save changes
    var changesCount = await _context.SaveChangesAsync();

    return Json(new
    {
        success = true,
        message = $"Auto-end check completed with {hoursThreshold} hour threshold. Changed {changesCount} schedules.",
        schedulesChanged = result.schedulesChanged,
        details = result
    });
}
    }
}