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
        private DateTime NowMYT()
        {
            var myt = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, myt);
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

                if (result.Success)
                {
                    // 2. SEND NOTIFICATIONS (New Logic)
                    var nowMYT = NowMYT();
                    var schedule = await _context.Schedules.FindAsync(request.ScheduleId);

                    // Notify Payer
                    _context.Notifications.Add(new Notification
                    {
                        UserId = request.UserId,
                        Message = $"Payment of RM{request.Amount:0.00} for '{schedule?.GameName}' successful. Funds are held in escrow.",
                        LinkUrl = $"/Schedule/Details/{request.ScheduleId}",
                        DateCreated = nowMYT,
                        IsRead = false
                    });

                    // Notify Host
                    var host = await _context.ScheduleParticipants
                        .FirstOrDefaultAsync(p => p.ScheduleId == request.ScheduleId && p.Role == ParticipantRole.Organizer);

                    if (host != null)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = host.UserId,
                            Message = $"New escrow payment received from a player for '{schedule?.GameName}'.",
                            LinkUrl = $"/Schedule/Details/{request.ScheduleId}",
                            DateCreated = nowMYT,
                            IsRead = false
                        });
                    }

                    await _context.SaveChangesAsync();
                }

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

        // POST: /Escrow/RaiseDispute
        [HttpPost]
        public async Task<IActionResult> RaiseDispute([FromBody] RaiseDisputeRequest request)
        {
            try
            {
                if (request == null || request.ScheduleId <= 0 || string.IsNullOrWhiteSpace(request.DisputeReason))
                {
                    return Json(new { success = false, message = "Invalid request. Schedule ID and dispute reason are required." });
                }

                var currentUserId = GetCurrentUserId();

                // Verify user is a participant in the schedule
                var isParticipant = await _context.ScheduleParticipants
                    .AnyAsync(p => p.ScheduleId == request.ScheduleId &&
                                 p.UserId == currentUserId &&
                                 p.Status == ParticipantStatus.Confirmed);

                if (!isParticipant)
                {
                    return Json(new { success = false, message = "You must be a confirmed participant in this game to raise a dispute." });
                }

                // Check if a dispute already exists for this schedule by this user
                var existingDispute = await _context.EscrowDisputes
                    .FirstOrDefaultAsync(d => d.ScheduleId == request.ScheduleId &&
                                            d.RaisedByUserId == currentUserId &&
                                            d.AdminDecision == "Pending");

                if (existingDispute != null)
                {
                    return Json(new { success = false, message = "You already have a pending dispute for this game." });
                }
                var nowMYT = NowMYT();
                // Create new dispute
                var dispute = new EscrowDispute
                {
                    ScheduleId = request.ScheduleId,
                    RaisedByUserId = currentUserId,
                    DisputeReason = request.DisputeReason.Trim(),
                    AdminDecision = "Pending",
                    CreatedAt = nowMYT,
                    UpdatedAt = nowMYT
                };

                _context.EscrowDisputes.Add(dispute);

                // 🔔 NOTIFY HOST (New)
                var host = await _context.ScheduleParticipants
                    .FirstOrDefaultAsync(p => p.ScheduleId == request.ScheduleId && p.Role == ParticipantRole.Organizer);

                if (host != null && host.UserId != currentUserId)
                {
                    var schedule = await _context.Schedules.FindAsync(request.ScheduleId);
                    _context.Notifications.Add(new Notification
                    {
                        UserId = host.UserId,
                        Message = $"A dispute has been raised for your game '{schedule?.GameName}'. Funds are held pending admin review.",
                        LinkUrl = $"/Schedule/Details/{request.ScheduleId}",
                        DateCreated = nowMYT,
                        IsRead = false
                    });
                }
                await _context.SaveChangesAsync();

                _logger.LogInformation("Dispute raised by user {UserId} for schedule {ScheduleId}", currentUserId, request.ScheduleId);

                return Json(new
                {
                    success = true,
                    message = "Dispute submitted successfully. Our admin team will review it shortly.",
                    disputeId = dispute.DisputeId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Server error",
                    detail = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }

        }

        [HttpPost]
        public async Task<IActionResult> SubmitRefundRequest(int scheduleId, string reason)
        {
            try
            {
                // Malaysia time
                var myt = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                var nowMYT = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, myt);

                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId == null)
                    return Json(new { success = false, message = "User not logged in." });

                if (string.IsNullOrWhiteSpace(reason))
                    return Json(new { success = false, message = "Please provide a refund reason." });

                // get user's escrow for this schedule
                var escrow = await _context.Escrows
                    .FirstOrDefaultAsync(e => e.ScheduleId == scheduleId && e.UserId == userId);

                if (escrow == null)
                    return Json(new { success = false, message = "No escrow payment found for this schedule." });

                // ✔ Block only pending refund requests, NOT previous ones
                var hasPending = await _context.RefundRequests
                    .AnyAsync(r => r.EscrowId == escrow.EscrowId && r.AdminDecision == "Pending");

                if (hasPending)
                    return Json(new { success = false, message = "You already have a refund request pending review." });

                // ✔ allow submitting again (new row)
                var request = new RefundRequest
                {
                    EscrowId = escrow.EscrowId,
                    UserId = userId.Value,       // FK to User
                    ReportedBy = userId.Value,   // For your logic
                    RefundReason = reason.Trim(),
                    AdminDecision = "Pending",
                    CreatedAt = nowMYT,
                    UpdatedAt = nowMYT
                };

                _context.RefundRequests.Add(request);
                // 🔔 NOTIFY HOST (New)
                var host = await _context.ScheduleParticipants
                    .FirstOrDefaultAsync(p => p.ScheduleId == scheduleId && p.Role == ParticipantRole.Organizer);

                if (host != null && host.UserId != userId)
                {
                    var schedule = await _context.Schedules.FindAsync(scheduleId);
                    _context.Notifications.Add(new Notification
                    {
                        UserId = host.UserId,
                        Message = $"A player has requested a refund for '{schedule?.GameName}'. Funds are held pending admin review.",
                        LinkUrl = $"/Schedule/Details/{scheduleId}",
                        DateCreated = nowMYT,
                        IsRead = false
                    });
                }
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Refund request submitted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Server error submitting refund request.",
                    detail = ex.Message
                });
            }
        }


    }

    // Request model for raising dispute
    public class RaiseDisputeRequest
    {
        public int ScheduleId { get; set; }
        public string? DisputeReason { get; set; }
    }
}