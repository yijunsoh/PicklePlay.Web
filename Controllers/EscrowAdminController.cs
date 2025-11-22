using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;

namespace PicklePlay.Controllers
{
    public class EscrowAdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public EscrowAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Helper — Malaysia Time (UTC+8)
        private DateTime NowMYT()
        {
            var myt = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, myt);
        }

        // ===============================
        // 🔵 ESCROW TRANSACTION OVERVIEW
        // ===============================
        public async Task<IActionResult> Index()
        {
            var escrows = await _context.Escrows
                .Include(e => e.User)
                .Include(e => e.Schedule)
                .Include(e => e.Schedule.Participants)
                .Include(e => e.Transactions)
                .OrderByDescending(e => e.ScheduleId)
                .ToListAsync();

            // GROUP BY SCHEDULE → ONE ROW PER GAME
            var grouped = escrows
                .GroupBy(e => e.ScheduleId)
                .Select(g =>
                {
                    var first = g.First(); // Shared schedule & host info

                    return new EscrowAdminViewModel
                    {
                        EscrowId = first.EscrowId,
                        ScheduleId = first.ScheduleId,
                        GameTitle = first.Schedule.GameName!,
                        GameEndTime = first.Schedule.EndTime,

                        HostUserId = first.Schedule.Participants
                            .FirstOrDefault(p => p.Role == ParticipantRole.Organizer)?.UserId,

                        HostName = first.Schedule.Participants
                            .FirstOrDefault(p => p.Role == ParticipantRole.Organizer)?.User?.Username ?? "-",

                        // Total players
                        PlayerCount = first.Schedule.Participants
                            .Count(p => p.Status == ParticipantStatus.Confirmed),

                        // Sum all players’ escrow amounts for this game
                        Amount = g.Sum(e => e.Transactions.Sum(t => t.Amount)),
                        TotalEscrowAmount = first.Schedule.TotalEscrowAmount,

                        // If ANY player has a dispute → show dispute
                        DisputeReason = _context.EscrowDisputes
                            .Where(d => d.ScheduleId == first.ScheduleId)
                            .Select(d => d.DisputeReason)
                            .FirstOrDefault(),

                        // Status — if any escrow is still held → show Held
                        Status = g.Any(e => e.Status == "Held")
                            ? "Held"
                            : g.Any(e => e.Status == "Refunded")
                                ? "Refunded"
                                : "Released"
                    };
                })
                .OrderByDescending(v => v.ScheduleId)
                .ToList();

            return View("~/Views/Admin/EscrowTransaction.cshtml", grouped);
        }




        // ======================================
        // 🔵 RELEASE ESCROW (SEND TO HOST WALLET)
        // ======================================
        [HttpPost]
        public async Task<IActionResult> ReleaseEscrow(int scheduleId)
        {
            var nowMYT = NowMYT();

            var escrows = await _context.Escrows
                .Include(e => e.Transactions)
                .Where(e => e.ScheduleId == scheduleId)
                .ToListAsync();

            if (!escrows.Any())
                return Json(new { success = false, message = "No escrow records found for this game." });

            var schedule = await _context.Schedules
                .Include(s => s.Participants)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            var host = schedule.Participants
                .FirstOrDefault(p => p.Role == ParticipantRole.Organizer);

            if (host == null)
                return Json(new { success = false, message = "Host not found." });

            var hostWallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == host.UserId);

            if (hostWallet == null)
                return Json(new { success = false, message = "Host wallet not found." });

            decimal totalAmount = 0;

            foreach (var e in escrows)
            {
                var payerWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == e.UserId);

                if (payerWallet == null) continue;

                decimal paid = e.Transactions.Sum(t => t.Amount);

                totalAmount += paid;
                payerWallet.EscrowBalance -= paid;

                if (payerWallet.EscrowBalance < 0)
                    payerWallet.EscrowBalance = 0;

                _context.Transactions.Add(new Transaction
                {
                    WalletId = payerWallet.WalletId,
                    EscrowId = e.EscrowId,
                    TransactionType = "Escrow_Released",
                    Amount = -paid,
                    PaymentMethod = "Wallet",
                    PaymentStatus = "Completed",
                    CreatedAt = nowMYT,
                    Description = $"Escrow released for schedule {scheduleId}"
                });

                e.Status = "Released";
            }

            hostWallet.WalletBalance += totalAmount;

            _context.Transactions.Add(new Transaction
            {
                WalletId = hostWallet.WalletId,
                TransactionType = "Escrow_Receive",
                Amount = totalAmount,
                PaymentMethod = "Wallet",
                PaymentStatus = "Completed",
                CreatedAt = nowMYT,
                Description = $"Host received escrow from schedule {scheduleId}"
            });

            schedule.EscrowStatus = "Released";

            if (schedule.ScheduleType == ScheduleType.Competition)
            {
                // Competition: Set to Completed (8)
                schedule.Status = ScheduleStatus.Completed;
            }
            else
            {
                // Regular Game: Set to Past (2)
                schedule.Status = ScheduleStatus.Past;
            }

            // 🔥 UPDATE DISPUTE STATUS
            var allDisputes = await _context.EscrowDisputes
    .Where(d => d.ScheduleId == scheduleId)
    .ToListAsync();

            foreach (var d in allDisputes)
            {
                d.AdminDecision = "Released";
                d.UpdatedAt = nowMYT;
            }


            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Escrow released successfully." });
        }


        // ==================================
        // 🔵 REFUND ESCROW BACK TO PLAYERS
        // ==================================
        [HttpPost]
        public async Task<IActionResult> RefundEscrow(int scheduleId)
        {
            var nowMYT = NowMYT();

            var escrows = await _context.Escrows
                .Include(e => e.Transactions)
                .Where(e => e.ScheduleId == scheduleId)
                .ToListAsync();

            if (!escrows.Any())
                return Json(new { success = false, message = "No escrow records found." });

            var schedule = await _context.Schedules
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            foreach (var e in escrows)
            {
                var payerWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == e.UserId);

                if (payerWallet == null) continue;

                decimal paid = e.Transactions.Sum(t => t.Amount);

                payerWallet.WalletBalance += paid;

                payerWallet.EscrowBalance -= paid;
                if (payerWallet.EscrowBalance < 0)
                    payerWallet.EscrowBalance = 0;

                _context.Transactions.Add(new Transaction
                {
                    WalletId = payerWallet.WalletId,
                    EscrowId = e.EscrowId,
                    TransactionType = "Escrow_Refund",
                    Amount = paid,
                    PaymentMethod = "Wallet",
                    PaymentStatus = "Completed",
                    CreatedAt = nowMYT,
                    Description = $"Escrow refunded for schedule {scheduleId}"
                });

                e.Status = "Refunded";
            }

            schedule.EscrowStatus = "Refunded";

            if (schedule.ScheduleType == ScheduleType.Competition)
            {
                // Competition: Set to Completed (8)
                schedule.Status = ScheduleStatus.Completed;
            }
            else
            {
                // Regular Game: Set to Past (2)
                schedule.Status = ScheduleStatus.Past;
            }

            // 🔥 UPDATE DISPUTE STATUS HERE
            var allDisputes = await _context.EscrowDisputes
    .Where(d => d.ScheduleId == scheduleId)
    .ToListAsync();

            foreach (var d in allDisputes)
            {
                d.AdminDecision = "Refunded";
                d.UpdatedAt = nowMYT;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Escrow refunded successfully." });
        }


        // ==================================
        // 🔵 REVIEW DISPUTE (ADMIN NOTE)
        // ==================================
        [HttpPost]
        public async Task<IActionResult> MarkDisputeReviewed(int disputeId, string adminNote)
        {
            var dispute = await _context.EscrowDisputes
                .FirstOrDefaultAsync(d => d.DisputeId == disputeId);

            if (dispute == null)
                return Json(new { success = false, message = "Dispute not found." });

            var scheduleId = dispute.ScheduleId;
            var nowMYT = NowMYT();

            // UPDATE ALL DISPUTES FOR THIS SCHEDULE
            var allDisputes = await _context.EscrowDisputes
                .Where(d => d.ScheduleId == scheduleId)
                .ToListAsync();

            foreach (var d in allDisputes)
            {
                d.AdminDecision = "Reviewed";
                d.AdminReviewNote = adminNote;
                d.UpdatedAt = nowMYT;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "All disputes for this schedule marked as Reviewed." });
        }


        [HttpPost]
        public async Task<IActionResult> ApproveRefund(int refundId)
        {
            var nowMYT = NowMYT();

            var request = await _context.RefundRequests
                .Include(r => r.Escrow)
                .FirstOrDefaultAsync(r => r.RefundId == refundId);

            if (request == null)
                return Json(new { success = false, message = "Refund request not found." });

            var escrow = await _context.Escrows
                .Include(e => e.Transactions)
                .FirstOrDefaultAsync(e => e.EscrowId == request.EscrowId);

            if (escrow == null)
                return Json(new { success = false, message = "Escrow not found." });

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == escrow.UserId);

            if (wallet == null)
                return Json(new { success = false, message = "User wallet not found." });

            decimal paid = escrow.Transactions.Sum(t => t.Amount);

            // Refund to wallet
            wallet.WalletBalance += paid;
            wallet.EscrowBalance -= paid;
            if (wallet.EscrowBalance < 0) wallet.EscrowBalance = 0;

            _context.Transactions.Add(new Transaction
            {
                WalletId = wallet.WalletId,
                EscrowId = escrow.EscrowId,
                TransactionType = "Refund_Approved",
                Amount = paid,
                PaymentMethod = "Wallet",
                PaymentStatus = "Completed",
                CreatedAt = nowMYT,
                Description = $"Refund approved for escrow {escrow.EscrowId}"
            });

            // Update ScheduleParticipant → Pending Payment
            var sp = await _context.ScheduleParticipants
                .FirstOrDefaultAsync(p => p.ScheduleId == escrow.ScheduleId && p.UserId == escrow.UserId);

            if (sp != null)
                sp.Status = (ParticipantStatus)1;

            // Update Escrow
            escrow.Status = "Refunded";

            // Update refund request
            request.AdminDecision = "Approved";
            request.DecisionDate = nowMYT;
            request.UpdatedAt = nowMYT;

            // Notification
            _context.Notifications.Add(new Notification
            {
                UserId = escrow.UserId,
                Message = $"Your refund request for schedule #{escrow.ScheduleId} was approved.",
                LinkUrl = "/Schedule/Details/" + escrow.ScheduleId,
                DateCreated = nowMYT,
                IsRead = false
            });

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Refund approved and processed." });
        }

        [HttpPost]
        public async Task<IActionResult> RejectRefund(int refundId, string adminNote)
        {
            var nowMYT = NowMYT();

            if (string.IsNullOrWhiteSpace(adminNote))
                return Json(new { success = false, message = "Please enter rejection reason." });

            var request = await _context.RefundRequests
                .Include(r => r.Escrow)
                .FirstOrDefaultAsync(r => r.RefundId == refundId);

            if (request == null)
                return Json(new { success = false, message = "Refund request not found." });

            // Update refund request
            request.AdminDecision = "Rejected";
            request.AdminNote = adminNote;      // Admin's reject reason (NEW)
            request.UpdatedAt = nowMYT;
            request.DecisionDate = nowMYT;

            // Ensure Escrow exists before dereferencing
            int scheduleId = request.Escrow!.ScheduleId;

            // Send notification to user
            _context.Notifications.Add(new Notification
            {
                UserId = request.ReportedBy,
                Message = $"Your refund request for schedule #{scheduleId} was rejected. Reason: {adminNote}",
                LinkUrl = "/Schedule/Details/" + scheduleId,
                DateCreated = nowMYT,
                IsRead = false
            });

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Refund rejected and user notified." });
        }


    }
}
