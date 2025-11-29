using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Helpers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Services
{
    public class AutoReleaseEscrowService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoReleaseEscrowService> _logger;

        public AutoReleaseEscrowService(
            IServiceProvider serviceProvider,
            ILogger<AutoReleaseEscrowService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auto Release Escrow Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndReleaseEscrows();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Auto Release Escrow Service");
                }
                // Check every 10 seconds
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task CheckAndReleaseEscrows()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var nowUtc = DateTime.UtcNow;

                _logger.LogInformation($"=== Auto Release Escrow Check Starting ===");
                _logger.LogInformation($"Current UTC Time: {nowUtc:yyyy-MM-dd HH:mm:ss}");

                // ==============================================================================
                // 1. CHECK FOR CANCELLED GAMES (Status 4) -> Refund Immediately
                // ==============================================================================
                var cancelledSchedules = await context.Schedules
                    .Include(s => s.Participants)
                    .Where(s => s.Status == ScheduleStatus.Cancelled &&
                                // ⬇️ FIX: Check for BOTH "Held" OR "InEscrow"
                                (s.EscrowStatus == "Held" || s.EscrowStatus == "InEscrow"))
                    .ToListAsync();

                if (cancelledSchedules.Any())
                {
                    _logger.LogInformation($"Found {cancelledSchedules.Count} CANCELLED schedules. Processing immediate refunds...");
                    foreach (var schedule in cancelledSchedules)
                    {
                        await ProcessCancelledRefunds(context, schedule);
                    }
                }

                // ==============================================================================
                // 2. CHECK FOR COMPLETED/PAST GAMES
                // ==============================================================================
                var schedulesToProcess = await context.Schedules
                    .Include(s => s.Participants)
                    .Include(s => s.Escrows)
                    .Include(s => s.EscrowDisputes)
                    .Where(s => (s.Status == ScheduleStatus.Completed || s.Status == ScheduleStatus.Past) &&
                                // Keep checking InEscrow for completed games
                                (s.EscrowStatus == "InEscrow" || s.EscrowStatus == "Held"))
                    .ToListAsync();

                _logger.LogInformation($"Found {schedulesToProcess.Count} auto-ended schedules with escrow to process");

                foreach (var schedule in schedulesToProcess)
                {
                    // ... (Rest of your existing logic) ...

                    // A. Check if escrows actually exist with 'Held' status
                    var hasHeldEscrows = await context.Escrows
                        .AnyAsync(e => e.ScheduleId == schedule.ScheduleId && e.Status == "Held");

                    if (!hasHeldEscrows)
                    {
                        // OPTIONAL: If Schedule says "InEscrow" but no money is held, auto-fix the status
                        _logger.LogWarning($"No held escrows found for schedule {schedule.ScheduleId}. Updating status to Released.");
                        schedule.EscrowStatus = "Released";
                        await context.SaveChangesAsync();
                        continue;
                    }

                    // ... (Continue with Dispute and Refund checks) ...
                    // B. Check for Pending Disputes
                    var hasPendingDisputes = await context.EscrowDisputes
                        .AnyAsync(d => d.ScheduleId == schedule.ScheduleId &&
                                        d.AdminDecision == "Pending");

                    // C. Check for Pending Refund Requests
                    var hasPendingRefunds = await context.RefundRequests
                        .Include(r => r.Escrow)
                        .AnyAsync(r => r.Escrow!.ScheduleId == schedule.ScheduleId &&
                                        r.AdminDecision == "Pending");

                    if (hasPendingDisputes)
                    {
                        _logger.LogInformation($"⏸️ Blocking release for schedule {schedule.ScheduleId} due to disputes");
                        await HandleBlockedRelease(context, schedule);
                    }
                    else if (hasPendingRefunds)
                    {
                        _logger.LogInformation($"⏸️ Blocking release for schedule {schedule.ScheduleId} due to pending refund requests");
                        await HandleBlockedByRefund(context, schedule);
                    }
                    else
                    {
                        _logger.LogInformation($"🚀 Auto-releasing escrow for schedule {schedule.ScheduleId}");
                        await AutoReleaseEscrow(context, schedule);
                    }
                }
                _logger.LogInformation($"=== Auto Release Escrow Check Complete ===");
            }
        }

        private async Task AutoReleaseEscrow(ApplicationDbContext context, Schedule schedule)
        {
            var nowMYT = DateTimeHelper.GetMalaysiaTime();

            try
            {
                _logger.LogInformation($"Processing escrow release for schedule {schedule.ScheduleId} ({schedule.GameName})");

                // 1. Get Host Info FIRST
                var host = await context.ScheduleParticipants
                    .Include(p => p.User)
                    .FirstOrDefaultAsync(p => p.ScheduleId == schedule.ScheduleId &&
                                             p.Role == ParticipantRole.Organizer);

                if (host?.User == null) return;

                var hostWallet = await context.Wallets.FirstOrDefaultAsync(w => w.UserId == host.UserId);
                if (hostWallet == null) return;

                // 2. Get all held escrows
                var escrows = await context.Escrows
                    .Include(e => e.Transactions)
                    .Include(e => e.User)
                    .Where(e => e.ScheduleId == schedule.ScheduleId && e.Status == "Held")
                    .ToListAsync();

                // Guard clause: If no escrows, mark schedule as released and exit
                if (!escrows.Any())
                {
                    schedule.EscrowStatus = "Released";
                    await context.SaveChangesAsync();
                    return;
                }

                decimal totalCollectedForHost = 0;
                int processedCount = 0;

                // 3. Process Players ONE BY ONE (Atomic Processing)
                foreach (var escrow in escrows)
                {
                    // 🛑 SAFETY CHECK: Re-query DB for this specific row
                    var isAlreadyReleased = await context.Escrows
                        .AnyAsync(e => e.EscrowId == escrow.EscrowId && e.Status == "Released");

                    if (isAlreadyReleased) continue;

                    var payerWallet = await context.Wallets.FirstOrDefaultAsync(w => w.UserId == escrow.UserId);
                    if (payerWallet == null) continue;

                    decimal paid = escrow.Transactions.Sum(t => t.Amount);
                    if (paid <= 0) continue;

                    // --- PLAYER TRANSACTION ---
                    payerWallet.EscrowBalance -= paid;
                    if (payerWallet.EscrowBalance < 0) payerWallet.EscrowBalance = 0;

                    context.Transactions.Add(new Transaction
                    {
                        WalletId = payerWallet.WalletId,
                        EscrowId = escrow.EscrowId,
                        TransactionType = "Escrow_Released",
                        Amount = -paid,
                        PaymentMethod = "Wallet",
                        PaymentStatus = "Completed",
                        CreatedAt = nowMYT,
                        Description = $"Escrow auto-released for schedule {schedule.ScheduleId}"
                    });

                    escrow.Status = "Released";

                    // Notify Player
                    context.Notifications.Add(new Notification
                    {
                        UserId = escrow.UserId,
                        Message = $"Your escrow payment of RM{paid:0.00} for '{schedule.GameName}' has been released to the host.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        IsRead = false
                    });

                    // Accumulate for Host
                    totalCollectedForHost += paid;
                    processedCount++;

                    // 🛑 SAVE IMMEDIATELY (Locks this player row)
                    await context.SaveChangesAsync();
                }

                // 4. Pay Host (Only if we actually processed new payments)
                if (totalCollectedForHost > 0)
                {
                    hostWallet.WalletBalance += totalCollectedForHost;

                    // Optional: Update TotalEarnings if you have such a field
                    // hostWallet.TotalEarnings += totalCollectedForHost; 

                    context.Transactions.Add(new Transaction
                    {
                        WalletId = hostWallet.WalletId,
                        TransactionType = "Escrow_Receive",
                        Amount = totalCollectedForHost,
                        PaymentMethod = "Wallet",
                        PaymentStatus = "Completed",
                        CreatedAt = nowMYT,
                        Description = $"Auto-received escrow from schedule {schedule.ScheduleId}"
                    });

                    context.Notifications.Add(new Notification
                    {
                        UserId = host.UserId,
                        Message = $"You have automatically received RM{totalCollectedForHost:0.00} escrow payment from {processedCount} players for '{schedule.GameName}'.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        IsRead = false
                    });

                    _logger.LogInformation($"Paid Host RM{totalCollectedForHost}");

                    // Save Host transaction
                    await context.SaveChangesAsync();
                }

                // 5. Final Schedule Status Cleanup
                bool anyHeldLeft = await context.Escrows.AnyAsync(e => e.ScheduleId == schedule.ScheduleId && e.Status == "Held");

                if (!anyHeldLeft)
                {
                    // Fetch fresh schedule context
                    var currentSchedule = await context.Schedules.FindAsync(schedule.ScheduleId);
                    if (currentSchedule != null && currentSchedule.EscrowStatus != "Released")
                    {
                        currentSchedule.EscrowStatus = "Released";

                        // Auto-resolve disputes
                        var disputes = await context.EscrowDisputes
                            .Where(d => d.ScheduleId == schedule.ScheduleId && d.AdminDecision == "Pending")
                            .ToListAsync();

                        foreach (var d in disputes)
                        {
                            d.AdminDecision = "Released";
                            d.UpdatedAt = nowMYT;
                        }

                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error auto-releasing escrow for schedule {schedule.ScheduleId}");
            }
        }

        private async Task ProcessCancelledRefunds(ApplicationDbContext context, Schedule schedule)
        {
            var nowMYT = DateTimeHelper.GetMalaysiaTime();
            _logger.LogInformation($"Processing refunds for CANCELLED schedule {schedule.ScheduleId}");

            try
            {
                // 1. Get all held escrows
                var escrows = await context.Escrows
                    .Include(e => e.Transactions)
                    .Include(e => e.User)
                    .Where(e => e.ScheduleId == schedule.ScheduleId && (e.Status == "Held" || e.Status == "InEscrow"))
                    .ToListAsync();

                if (!escrows.Any())
                {
                    schedule.EscrowStatus = "Refunded";
                    await context.SaveChangesAsync();
                    return;
                }

                // 2. Refund each player individually
                foreach (var escrow in escrows)
                {
                    // 🛑 CRITICAL: Check DB immediately before processing
                    // This prevents Run B from processing if Run A just finished this specific row
                    var isProcessed = await context.Escrows
                        .AnyAsync(e => e.EscrowId == escrow.EscrowId && e.Status == "Refunded");

                    if (isProcessed) continue;

                    var payerWallet = await context.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == escrow.UserId);

                    if (payerWallet == null) continue;

                    decimal paidAmount = escrow.Transactions.Sum(t => t.Amount);
                    if (paidAmount <= 0) continue;

                    // --- REFUND LOGIC ---
                    payerWallet.WalletBalance += paidAmount;
                    payerWallet.EscrowBalance -= paidAmount;
                    if (payerWallet.EscrowBalance < 0) payerWallet.EscrowBalance = 0;

                    if (payerWallet.TotalSpent >= paidAmount)
                        payerWallet.TotalSpent -= paidAmount;

                    context.Transactions.Add(new Transaction
                    {
                        WalletId = payerWallet.WalletId,
                        EscrowId = escrow.EscrowId,
                        TransactionType = "Escrow_Refund",
                        Amount = paidAmount,
                        PaymentMethod = "Wallet",
                        PaymentStatus = "Completed",
                        CreatedAt = nowMYT,
                        Description = $"Refund for cancelled game: {schedule.GameName}"
                    });

                    // Update Escrow Status
                    escrow.Status = "Refunded";

                    // Notify Player
                    context.Notifications.Add(new Notification
                    {
                        UserId = escrow.UserId,
                        Message = $"The game '{schedule.GameName}' has been cancelled. RM{paidAmount:0.00} has been refunded.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        IsRead = false
                    });

                    // 🛑 CRITICAL: Save AFTER EACH PLAYER
                    // This locks the transaction immediately so the next run sees it as "Refunded"
                    await context.SaveChangesAsync();
                }

                // 3. Final cleanup (Schedule Status)
                // Re-fetch schedule to ensure we have latest state
                var currentSchedule = await context.Schedules.FindAsync(schedule.ScheduleId);
                if (currentSchedule != null && currentSchedule.EscrowStatus != "Refunded")
                {
                    currentSchedule.EscrowStatus = "Refunded";

                    // Clean up disputes
                    var pendingDisputes = await context.EscrowDisputes
                        .Where(d => d.ScheduleId == schedule.ScheduleId && d.AdminDecision == "Pending")
                        .ToListAsync();

                    foreach (var d in pendingDisputes)
                    {
                        d.AdminDecision = "Refunded";
                        d.UpdatedAt = nowMYT;
                    }

                    // Notify Host
                    var host = await context.ScheduleParticipants
                        .FirstOrDefaultAsync(p => p.ScheduleId == schedule.ScheduleId && p.Role == ParticipantRole.Organizer);

                    if (host != null)
                    {
                        context.Notifications.Add(new Notification
                        {
                            UserId = host.UserId,
                            Message = $"Game '{schedule.GameName}' cancelled. Fees refunded to players.",
                            LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                            DateCreated = nowMYT,
                            IsRead = false
                        });
                    }

                    await context.SaveChangesAsync();
                }

                _logger.LogInformation($"✅ Successfully processed refunds for schedule {schedule.ScheduleId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing refunds for cancelled schedule {schedule.ScheduleId}");
            }
        }

        private async Task HandleBlockedRelease(ApplicationDbContext context, Schedule schedule)
        {
            var nowMYT = DateTimeHelper.GetMalaysiaTime();

            _logger.LogInformation($"⏸️ Escrow release blocked for schedule {schedule.ScheduleId} due to pending disputes");

            // UPDATE STATUS so it won't be processed again by auto-service
            schedule.EscrowStatus = "BlockedByDispute";
            _logger.LogInformation($"Updated schedule {schedule.ScheduleId} EscrowStatus to 'BlockedByDispute'");

            // Include User for host
            var host = await context.ScheduleParticipants
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.ScheduleId == schedule.ScheduleId &&
                                         p.Role == ParticipantRole.Organizer);

            if (host != null && host.User != null)
            {
                // Notify host about blocked release
                var hostNotification = new Notification
                {
                    UserId = host.UserId,
                    Message = $"Escrow release for '{schedule.GameName}' is delayed due to pending disputes. The amount will be held until admin review is completed.",
                    LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                    DateCreated = nowMYT,
                    CreatedAt = nowMYT,
                    IsRead = false
                };
                context.Notifications.Add(hostNotification);
                _logger.LogInformation($"Notified host {host.User.Username} about blocked escrow release");
            }
            else
            {
                _logger.LogWarning($"Host or host user not found for schedule {schedule.ScheduleId}");
            }

            // Notify all players who raised disputes (include User)
            var disputes = await context.EscrowDisputes
                .Include(d => d.RaisedByUser)
                .Where(d => d.ScheduleId == schedule.ScheduleId && d.AdminDecision == "Pending")
                .ToListAsync();

            foreach (var dispute in disputes)
            {
                if (dispute.RaisedByUser != null)
                {
                    var playerNotification = new Notification
                    {
                        UserId = dispute.RaisedByUserId,
                        Message = $"Your dispute for '{schedule.GameName}' is under admin review. Escrow payments are currently held.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        CreatedAt = nowMYT,
                        IsRead = false
                    };
                    context.Notifications.Add(playerNotification);
                    _logger.LogInformation($"Notified player {dispute.RaisedByUser.Username} about dispute review");
                }
                else
                {
                    _logger.LogWarning($"RaisedByUser not found for dispute {dispute.DisputeId}");
                }
            }

            await context.SaveChangesAsync();
            _logger.LogInformation($"Notified host and {disputes.Count} players about blocked escrow release for schedule {schedule.ScheduleId}");
        }


        private async Task HandleBlockedByRefund(ApplicationDbContext context, Schedule schedule)
        {
            var nowMYT = DateTimeHelper.GetMalaysiaTime();

            _logger.LogInformation($"⏸️ Escrow release blocked for schedule {schedule.ScheduleId} due to pending refund requests");

            // UPDATE STATUS to 'BlockedByRefund'
            schedule.EscrowStatus = "BlockedByRefund";
            _logger.LogInformation($"Updated schedule {schedule.ScheduleId} EscrowStatus to 'BlockedByRefund'");

            // Notify Host
            var host = await context.ScheduleParticipants
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.ScheduleId == schedule.ScheduleId &&
                                         p.Role == ParticipantRole.Organizer);

            if (host != null && host.User != null)
            {
                context.Notifications.Add(new Notification
                {
                    UserId = host.UserId,
                    Message = $"Escrow release for '{schedule.GameName}' is delayed due to a pending refund request. Funds are held until admin review.",
                    LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                    DateCreated = nowMYT,
                    CreatedAt = nowMYT,
                    IsRead = false
                });
            }

            // Notify Users who requested refunds
            var refunds = await context.RefundRequests
                .Include(r => r.Escrow)
                .Where(r => r.Escrow!.ScheduleId == schedule.ScheduleId && r.AdminDecision == "Pending")
                .ToListAsync();

            foreach (var req in refunds)
            {
                context.Notifications.Add(new Notification
                {
                    UserId = req.ReportedBy,
                    Message = $"Your refund request for '{schedule.GameName}' is under admin review. Escrow payments are currently held.",
                    LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                    DateCreated = nowMYT,
                    CreatedAt = nowMYT,
                    IsRead = false
                });
            }

            await context.SaveChangesAsync();
            _logger.LogInformation($"Notified host and {refunds.Count} users about blocked escrow release for schedule {schedule.ScheduleId}");
        }
    }
}