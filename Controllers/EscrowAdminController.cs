using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;

namespace PicklePlay.Controllers
{
    public class EscrowAdminController : Controller
    {
        private readonly Data.ApplicationDbContext _context;

        public EscrowAdminController(Data.ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /EscrowAdmin
       public async Task<IActionResult> Index()
{
    var grouped = await _context.Escrows
        .Include(e => e.User)
        .Include(e => e.Schedule)
            .ThenInclude(s => s.CreatedByUser)   // Host user
        .Include(e => e.Schedule)
            .ThenInclude(s => s.Participants)    // For player count
                .ThenInclude(p => p.User)
        .Include(e => e.Transactions)
        .GroupBy(e => e.ScheduleId)
        .ToListAsync();

    var vmList = new List<EscrowAdminViewModel>();

    foreach (var group in grouped)
    {
        var schedule = group.First().Schedule;

        // Host user comes from Schedule.CreatedByUserId
        var hostUser = schedule.CreatedByUser;

        // Total escrow from all players
        var totalAmount = group
            .SelectMany(e => e.Transactions)
            .Sum(t => t.Amount);

        vmList.Add(new EscrowAdminViewModel
        {
            EscrowId = group.First().EscrowId,
            ScheduleId = schedule.ScheduleId,
            GameTitle = schedule.GameName!,
            GameEndTime = schedule.EndTime,

            // HOST INFO
            HostUserId = schedule.CreatedByUserId,
            HostName = hostUser?.Username ?? "-",

            // Amount of all player payments
            Amount = totalAmount,

            // Player count
            PlayerCount = schedule.Participants
                .Count(p => p.Role == ParticipantRole.Player),

            // Status logic
            Status = group.Any(e => e.Status == "Disputed") ? "Disputed"
                    : group.All(e => e.Status == "Released") ? "Released"
                    : group.All(e => e.Status == "Refunded") ? "Refunded"
                    : group.Any(e => e.Status == "Held") ? "Held"
                    : "Pending"
        });
    }

    return View("~/Views/Admin/EscrowTransaction.cshtml", vmList);
}



        [HttpPost]
        public async Task<IActionResult> ReleaseEscrow(int scheduleId)
        {
            // Fetch all escrow entries for the whole game
            var escrows = await _context.Escrows
                .Include(e => e.Transactions)
                .Where(e => e.ScheduleId == scheduleId)
                .ToListAsync();

            if (!escrows.Any())
                return Json(new { success = false, message = "No escrow records found for this game." });

            // Load Game
            var schedule = await _context.Schedules
                .Include(s => s.Participants)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            // Find Host
            var host = schedule.Participants.FirstOrDefault(p => p.Role == ParticipantRole.Organizer);
            if (host == null)
                return Json(new { success = false, message = "Host not found." });

            var hostWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == host.UserId);
            if (hostWallet == null)
                return Json(new { success = false, message = "Host wallet not found." });

            decimal totalAmount = 0;

            // Loop through each player's escrow entry
            foreach (var e in escrows)
            {
                decimal userPaid = e.Transactions.Sum(t => t.Amount);
                totalAmount += userPaid;

                // Deduct from player's escrow balance
                var payerWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == e.UserId);

                if (payerWallet != null)
                {
                    payerWallet.EscrowBalance -= userPaid;
                    if (payerWallet.EscrowBalance < 0)
                        payerWallet.EscrowBalance = 0;
                }

                // Mark escrow released
                e.Status = "Released";
            }

            // Add final total to host balance
            hostWallet.WalletBalance += totalAmount;

            // Update schedule escrow status
            schedule.EscrowStatus = "Released";

            await _context.SaveChangesAsync();

            TempData["Success"] = "Escrow released to host successfully.";
            return RedirectToAction("EscrowTransaction", "Admin");
        }


        [HttpPost]
        public async Task<IActionResult> RefundEscrow(int scheduleId)
        {
            // Fetch all escrow records for this schedule
            var escrows = await _context.Escrows
                .Include(e => e.Transactions)
                .Where(e => e.ScheduleId == scheduleId)
                .ToListAsync();

            if (!escrows.Any())
                return Json(new { success = false, message = "No escrow records found for this game." });

            // Load schedule
            var schedule = await _context.Schedules
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null)
                return Json(new { success = false, message = "Schedule not found." });

            foreach (var e in escrows)
            {
                decimal amountPaid = e.Transactions.Sum(t => t.Amount);

                // Return money to payer
                var payerWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == e.UserId);

                if (payerWallet != null)
                {
                    // Deduct from escrow balance
                    payerWallet.EscrowBalance -= amountPaid;
                    if (payerWallet.EscrowBalance < 0)
                        payerWallet.EscrowBalance = 0;

                    // Add back to wallet
                    payerWallet.WalletBalance += amountPaid;
                }

                // Update escrow status
                e.Status = "Refunded";
            }

            // Update schedule escrow status
            schedule.EscrowStatus = "Refunded";

            await _context.SaveChangesAsync();

            TempData["Success"] = "All players refunded successfully.";
            return RedirectToAction("EscrowTransaction", "Admin");
        }
    }
}
