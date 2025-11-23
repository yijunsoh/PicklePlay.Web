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

                // Check every 30 minutes (same as auto-end service)
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        private async Task CheckAndReleaseEscrows()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var nowUtc = DateTime.UtcNow;

                // ADD DETAILED DEBUG LOGGING
                _logger.LogInformation($"=== Auto Release Escrow Check Starting ===");
                _logger.LogInformation($"Current UTC Time: {nowUtc:yyyy-MM-dd HH:mm:ss}");

                // FIX: Check Schedule.EscrowStatus instead of Escrow.Status
                var schedulesToProcess = await context.Schedules
                    .Include(s => s.Participants)
                    .Include(s => s.Escrows)
                    .Include(s => s.EscrowDisputes)
                    .Where(s => (s.Status == ScheduleStatus.Completed || s.Status == ScheduleStatus.Past) &&
                               s.EscrowStatus == "InEscrow")
                    .ToListAsync();

                _logger.LogInformation($"Found {schedulesToProcess.Count} auto-ended schedules with escrow to process");

                // DEBUG: Log each schedule found
                foreach (var schedule in schedulesToProcess)
                {
                    _logger.LogInformation($"Processing Schedule {schedule.ScheduleId}: {schedule.GameName}");
                    _logger.LogInformation($"  - Status: {schedule.Status}");
                    _logger.LogInformation($"  - EscrowStatus: {schedule.EscrowStatus}");

                    var escrowCount = await context.Escrows
                        .CountAsync(e => e.ScheduleId == schedule.ScheduleId && e.Status == "Held");
                    var disputeCount = await context.EscrowDisputes
                        .CountAsync(d => d.ScheduleId == schedule.ScheduleId && d.AdminDecision == "Pending");

                    _logger.LogInformation($"  - Held Escrows: {escrowCount}");
                    _logger.LogInformation($"  - Pending Disputes: {disputeCount}");
                }

                foreach (var schedule in schedulesToProcess)
                {
                    // Check if escrows actually exist with 'Held' status
                    var hasHeldEscrows = await context.Escrows
                        .AnyAsync(e => e.ScheduleId == schedule.ScheduleId && e.Status == "Held");

                    if (!hasHeldEscrows)
                    {
                        _logger.LogWarning($"No held escrows found for schedule {schedule.ScheduleId}");
                        continue;
                    }

                    var hasPendingDisputes = await context.EscrowDisputes
                        .AnyAsync(d => d.ScheduleId == schedule.ScheduleId &&
                                     d.AdminDecision == "Pending");

                    if (hasPendingDisputes)
                    {
                        _logger.LogInformation($"⏸️ Blocking release for schedule {schedule.ScheduleId} due to disputes");
                        await HandleBlockedRelease(context, schedule);
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
    }
}