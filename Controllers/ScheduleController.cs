using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Linq; // Needed for Select
using Microsoft.EntityFrameworkCore; // *** ADD THIS ***
using System.Threading.Tasks; // *** ADD THIS ***

namespace PicklePlay.Controllers
{
    public class ScheduleController : Controller
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly ApplicationDbContext _context; // Add this
    private readonly IAuthService _authService; // Add this

        public ScheduleController(IScheduleRepository scheduleRepository, 
                              ApplicationDbContext context, // Add this
                              IAuthService authService) // Add this
    {
        _scheduleRepository = scheduleRepository;
        _context = context; // Add this
        _authService = authService; // Add this
    }

       
        // *** MODIFIED THIS ACTION ***
        public async Task<IActionResult> GameListing()
        {
            // We use _context directly to be able to use .Include()
            // This pulls in the Participants list for each schedule
            var schedules = await _context.Schedules
                                          .Include(s => s.Participants)
                                          .ToListAsync();
            return View(schedules);
        }

        // *** ALSO MODIFIED THIS ACTION (to fix the Details page) ***
        public async Task<IActionResult> Details(int id)
        {
            // We must use .Include() here as well, or the Details page won't see any participants
            var schedule = await _context.Schedules
                                         .Include(s => s.Participants)
                                             .ThenInclude(p => p.User) // This gets the User info (name, pic) for each participant
                                         .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound(); // Or return a specific "Not Found" view
            }
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

                // One-Off specific
                StartTime = schedule.StartTime, // Keep as DateTime?
                

                // Recurring specific
                RecurringWeek = new List<RecurringWeek>(), // Initialize
                RecurringStartTime = schedule.StartTime.HasValue ? TimeOnly.FromDateTime(schedule.StartTime.Value) : null,
                AutoCreateWhen = schedule.AutoCreateWhen ?? Models.AutoCreateWhen.B2d
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
                 case DayOfWeek.Monday:    todayDayFlag = RecurringWeek.Mon; break;
                 case DayOfWeek.Tuesday:   todayDayFlag = RecurringWeek.Tue; break;
                 case DayOfWeek.Wednesday: todayDayFlag = RecurringWeek.Wed; break;
                 case DayOfWeek.Thursday:  todayDayFlag = RecurringWeek.Thur; break;
                 case DayOfWeek.Friday:    todayDayFlag = RecurringWeek.Fri; break;
                 case DayOfWeek.Saturday:  todayDayFlag = RecurringWeek.Sat; break;
                 case DayOfWeek.Sunday:    todayDayFlag = RecurringWeek.Sun; break;
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
         // Check if AutoCreateWhen is provided (it has [Required] attribute, but good practice)
        if (!vm.AutoCreateWhen.HasValue)
        {
            ModelState.AddModelError("AutoCreateWhen", "Auto-Create When is required for Recurring schedules.");
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
        scheduleToUpdate.FeeAmount = (vm.FeeType == FeeType.AutoSplitTotal || vm.FeeType == FeeType.PerPerson) ? vm.FeeAmount : null;
        scheduleToUpdate.MinRankRestriction = vm.MinRankRestriction;
        scheduleToUpdate.MaxRankRestriction = vm.MaxRankRestriction;
        scheduleToUpdate.GenderRestriction = vm.GenderRestriction;
        scheduleToUpdate.AgeGroupRestriction = vm.AgeGroupRestriction;
        scheduleToUpdate.CancellationFreeze = vm.CancellationFreeze;
        scheduleToUpdate.HostRole = vm.HostRole;

        // Update type-specific fields
        if (vm.ScheduleType == ScheduleType.Recurring)
        {
            if(vm.RecurringStartTime.HasValue) {
                scheduleToUpdate.StartTime = DateTime.Today.Add(vm.RecurringStartTime.Value.ToTimeSpan());
            } else {
                 scheduleToUpdate.StartTime = null; // Should be caught by validation
            }
            scheduleToUpdate.AutoCreateWhen = vm.AutoCreateWhen; // Already checked for null
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
            scheduleToUpdate.AutoCreateWhen = null;
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
    }
}