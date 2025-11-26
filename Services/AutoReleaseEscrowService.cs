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
                // Check every 1 second
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
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
                _logger.LogInformation($"Releasing escrow for schedule {schedule.ScheduleId} ({schedule.GameName})");

                // Include User for escrows
                var escrows = await context.Escrows
                    .Include(e => e.Transactions)
                    .Include(e => e.User) // Add this
                    .Where(e => e.ScheduleId == schedule.ScheduleId && e.Status == "Held")
                    .ToListAsync();

                if (!escrows.Any())
                {
                    _logger.LogWarning($"No held escrows found for schedule {schedule.ScheduleId}");
                    return;
                }

                // Include User for host
                var host = await context.ScheduleParticipants
                    .Include(p => p.User) // Add this
                    .FirstOrDefaultAsync(p => p.ScheduleId == schedule.ScheduleId &&
                                             p.Role == ParticipantRole.Organizer);

                if (host == null)
                {
                    _logger.LogWarning($"Host not found for schedule {schedule.ScheduleId}");
                    return;
                }

                if (host.User == null)
                {
                    _logger.LogWarning($"Host user data not found for user ID {host.UserId}");
                    return;
                }

                var hostWallet = await context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == host.UserId);

                if (hostWallet == null)
                {
                    _logger.LogWarning($"Host wallet not found for user {host.User.Username}");
                    return;
                }

                decimal totalAmount = 0;

                // Release each player's escrow
                foreach (var escrow in escrows)
                {

                    bool alreadyReleased = await context.Transactions
    .AnyAsync(t => t.EscrowId == escrow.EscrowId && t.TransactionType == "Escrow_Released");

                    if (alreadyReleased)
                    {
                        escrow.Status = "Released";
                        continue; // SKIP this escrow if already processed
                    }
                    var payerWallet = await context.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == escrow.UserId);

                    if (payerWallet == null)
                    {
                        _logger.LogWarning($"Payer wallet not found for user {escrow.UserId}");
                        continue;
                    }

                    decimal paid = escrow.Transactions.Sum(t => t.Amount);

                    // ADD THIS DEBUG CHECK:
                    _logger.LogInformation($"Escrow {escrow.EscrowId}: {escrow.Transactions.Count} transactions, Total: RM{paid}");

                    if (paid <= 0)
                    {
                        _logger.LogWarning($"No valid payment amount for escrow {escrow.EscrowId}");
                        continue;
                    }

                    totalAmount += paid;

                    // Deduct from payer's escrow balance
                    payerWallet.EscrowBalance -= paid;
                    if (payerWallet.EscrowBalance < 0)
                        payerWallet.EscrowBalance = 0;

                    // Add transaction record
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

                    // Send notification to payer
                    var payerNotification = new Notification
                    {
                        UserId = escrow.UserId,
                        Message = $"Your escrow payment of RM{paid} for '{schedule.GameName}' has been automatically released to the host.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        CreatedAt = nowMYT,
                        IsRead = false
                    };
                    context.Notifications.Add(payerNotification);

                    // Safe logging for payer username
                    var payerUsername = escrow.User?.Username ?? "Unknown";
                    _logger.LogInformation($"Notified payer {payerUsername} about escrow release");
                }

                // Add to host's wallet
                hostWallet.WalletBalance += totalAmount;

                // Add transaction record for host
                context.Transactions.Add(new Transaction
                {
                    WalletId = hostWallet.WalletId,
                    TransactionType = "Escrow_Receive",
                    Amount = totalAmount,
                    PaymentMethod = "Wallet",
                    PaymentStatus = "Completed",
                    CreatedAt = nowMYT,
                    Description = $"Auto-received escrow from schedule {schedule.ScheduleId}"
                });

                // Update schedule escrow status
                schedule.EscrowStatus = "Released";

                // Update disputes status
                var allDisputes = await context.EscrowDisputes
                    .Where(d => d.ScheduleId == schedule.ScheduleId)
                    .ToListAsync();

                foreach (var dispute in allDisputes)
                {
                    dispute.AdminDecision = "Released";
                    dispute.UpdatedAt = nowMYT;
                }

                // Send notification to host
                var hostNotification = new Notification
                {
                    UserId = host.UserId,
                    Message = $"You have automatically received RM{totalAmount} escrow payment from {escrows.Count} players for '{schedule.GameName}'.",
                    LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                    DateCreated = nowMYT,
                    CreatedAt = nowMYT,
                    IsRead = false
                };
                context.Notifications.Add(hostNotification);

                await context.SaveChangesAsync();

                _logger.LogInformation($"✅ Successfully auto-released escrow for schedule {schedule.ScheduleId}");
                _logger.LogInformation($"   - Amount: RM{totalAmount}");
                _logger.LogInformation($"   - Players: {escrows.Count}");
                _logger.LogInformation($"   - Host: {host.User.Username}"); // Now safe

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
                // 1. Get all held escrows (payments) for this schedule
                var escrows = await context.Escrows
                    .Include(e => e.Transactions)
                    .Include(e => e.User)
                    .Where(e => e.ScheduleId == schedule.ScheduleId && (e.Status == "Held" || e.Status == "InEscrow"))
                    .ToListAsync();

                if (!escrows.Any())
                {
                    // If no money was held, just mark as refunded to stop future checks
                    schedule.EscrowStatus = "Refunded";
                    await context.SaveChangesAsync();
                    return;
                }

                // 2. Refund each player individually
                foreach (var escrow in escrows)
                {
                    bool alreadyRefunded = await context.Transactions
                        .AnyAsync(t => t.EscrowId == escrow.EscrowId && t.TransactionType == "Escrow_Refund");

                    if (alreadyRefunded)
                    {
                        escrow.Status = "Refunded";
                        continue; // SKIP this escrow if already processed
                    }
                    var payerWallet = await context.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == escrow.UserId);

                    if (payerWallet == null) continue;

                    decimal paidAmount = escrow.Transactions.Sum(t => t.Amount);
                    if (paidAmount <= 0) continue;

                    // A. Update Wallet: Return money from EscrowBalance to WalletBalance
                    payerWallet.WalletBalance += paidAmount;
                    payerWallet.EscrowBalance -= paidAmount;

                    // Safety check to prevent negative balance
                    if (payerWallet.EscrowBalance < 0) payerWallet.EscrowBalance = 0;

                    if (payerWallet.TotalSpent >= paidAmount)
                    {
                        payerWallet.TotalSpent -= paidAmount;
                    }

                    // B. Add Refund Transaction Record
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

                    // C. Update Escrow Status
                    escrow.Status = "Refunded";

                    // D. Notify Player
                    context.Notifications.Add(new Notification
                    {
                        UserId = escrow.UserId,
                        Message = $"The game '{schedule.GameName}' has been cancelled by the host. Your fee of RM{paidAmount:0.00} has been automatically refunded to your wallet.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        CreatedAt = nowMYT,
                        IsRead = false
                    });
                }

                // 3. Update Schedule Escrow Status
                schedule.EscrowStatus = "Refunded";

                // 4. Notify Host (Organizer)
                var host = schedule.Participants
                    .FirstOrDefault(p => p.Role == ParticipantRole.Organizer);

                if (host != null)
                {
                    context.Notifications.Add(new Notification
                    {
                        UserId = host.UserId,
                        Message = $"Game '{schedule.GameName}' cancellation successful. All collected fees have been automatically refunded to the players.",
                        LinkUrl = $"/Schedule/Details/{schedule.ScheduleId}",
                        DateCreated = nowMYT,
                        CreatedAt = nowMYT,
                        IsRead = false
                    });
                }

                // 5. Clean up any pending disputes (auto-resolve them as Refunded)
                var pendingDisputes = await context.EscrowDisputes
                    .Where(d => d.ScheduleId == schedule.ScheduleId && d.AdminDecision == "Pending")
                    .ToListAsync();

                foreach (var d in pendingDisputes)
                {
                    d.AdminDecision = "Refunded";
                    d.UpdatedAt = nowMYT;
                }

                await context.SaveChangesAsync();
                _logger.LogInformation($"✅ Successfully refunded {escrows.Count} players for cancelled schedule {schedule.ScheduleId}");
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