using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PicklePlay.Services;
using PicklePlay.Models;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Controllers
{
    public class EscrowController : Controller
    {
        private readonly IEscrowService _escrowService;
        private readonly Data.ApplicationDbContext _context;
        private readonly ILogger<EscrowController> _logger;

        public EscrowController(IEscrowService escrowService, Data.ApplicationDbContext context, ILogger<EscrowController> logger)
        {
            _escrowService = escrowService;
            _context = context;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId.HasValue) return userId.Value;

            // Fallback to claims if session is not available
            var userIdString = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdString, out int parsedUserId))
            {
                // If we found it via claims, put it in the session for the next request
                HttpContext.Session.SetInt32("UserId", parsedUserId);
                return parsedUserId;
            }

            // This exception should be caught by the action's try/catch
            throw new InvalidOperationException("User identity is not available or session expired.");
        }

        // POST: /Escrow/ProcessPayment
        [HttpPost]
        public async Task<IActionResult> ProcessPayment([FromBody] EscrowPaymentRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid request payload."
                    });
                }

                if (request.Amount <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid payment amount."
                    });
                }

                var result = await _escrowService.ProcessPaymentAsync(
                    request.ScheduleId,
                    request.UserId,
                    request.Amount,
                    request.PaymentType
                );

                return Ok(new
                {
                    success = result.Success,
                    message = result.Message,
                    escrowId = result.EscrowId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Server error occurred during payment.",
                    detail = ex.Message
                });
            }
        }



        // GET: /Escrow/CheckBalance
        [HttpGet]
        public async Task<IActionResult> CheckBalance(decimal amount)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var canAfford = await _escrowService.CanUserAffordPaymentAsync(currentUserId, amount);

                var wallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == currentUserId);

                return Json(new
                {
                    success = true,
                    canAfford = canAfford,
                    currentBalance = wallet?.WalletBalance ?? 0,
                    requiredAmount = amount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking balance for user");
                return Json(new { success = false, message = "Failed to check balance." });
            }
        }

        // GET: /Escrow/GetEscrowStatus
        [HttpGet]
        public async Task<IActionResult> GetEscrowStatus(int scheduleId)
        {
            try
            {
                var status = await _escrowService.GetEscrowStatusAsync(scheduleId);
                return Json(new { success = true, data = status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting escrow status for schedule {ScheduleId}", scheduleId);
                return Json(new { success = false, message = "Failed to get escrow status." });
            }
        }

        // POST: /Escrow/ReleaseEscrow (for organizers/admin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Organizer")]
        public async Task<IActionResult> ReleaseEscrow(int scheduleId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // Verify user has permission to release escrow
                var isOrganizer = await _context.ScheduleParticipants
                    .AnyAsync(p => p.ScheduleId == scheduleId &&
                                 p.UserId == currentUserId &&
                                 p.Role == ParticipantRole.Organizer);

                if (!isOrganizer && !User.IsInRole("Admin"))
                {
                    return Json(new { success = false, message = "You don't have permission to release escrow." });
                }

                // This would be implemented when you add the release functionality
                // var result = await _escrowService.ReleaseEscrowAsync(scheduleId, currentUserId);

                return Json(new { success = false, message = "Escrow release functionality coming soon." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing escrow for schedule {ScheduleId}", scheduleId);
                return Json(new { success = false, message = "An error occurred while releasing escrow." });
            }
        }

    }


}