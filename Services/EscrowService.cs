using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Threading.Tasks;

namespace PicklePlay.Services
{
    public class EscrowService : IEscrowService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EscrowService> _logger;

        public EscrowService(ApplicationDbContext context, ILogger<EscrowService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // For the "Sufficient balance" check in the modal
        public async Task<bool> CanUserAffordPaymentAsync(int userId, decimal amount)
        {
            var wallet = await _context.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null) return false;
            return wallet.WalletBalance >= amount;
        }

        public async Task<EscrowPaymentResult> ProcessPaymentAsync(
    int scheduleId,
    int userId,
    decimal amount,
    string paymentType)
        {
            try
            {
                if (amount <= 0)
                {
                    return new EscrowPaymentResult
                    {
                        Success = false,
                        Message = "Invalid payment amount."
                    };
                }

                // 1️⃣ Load schedule and wallet
                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

                if (schedule == null)
                {
                    return new EscrowPaymentResult
                    {
                        Success = false,
                        Message = "Schedule not found."
                    };
                }

                var wallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == userId);

                if (wallet == null)
                {
                    return new EscrowPaymentResult
                    {
                        Success = false,
                        Message = "Wallet not found."
                    };
                }

                if (wallet.WalletBalance < amount)
                {
                    return new EscrowPaymentResult
                    {
                        Success = false,
                        Message = "Insufficient wallet balance."
                    };
                }

                // 2️⃣ Everything from here is in a DB transaction
                using var dbTx = await _context.Database.BeginTransactionAsync();

                // 2a) Create or reuse escrow for this user + schedule
                var escrow = await _context.Escrows
                    .FirstOrDefaultAsync(e => e.ScheduleId == scheduleId &&
                                              e.UserId == userId);

                if (escrow == null)
                {
                    escrow = new Escrow
                    {
                        ScheduleId = scheduleId,
                        UserId = userId,
                        Status = "Held",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Escrows.Add(escrow);
                }
                else
                {
                    escrow.Status = "Held";
                }

                // 2b) Move money from wallet -> escrow
                wallet.WalletBalance -= amount;
                wallet.EscrowBalance += amount;
                wallet.TotalSpent += amount;
                wallet.LastUpdated = DateTime.UtcNow;

                // 2c) Update schedule total escrow amount (running sum)
                schedule.TotalEscrowAmount += amount;   // e.g. 20 + 10 = 30
                schedule.EscrowStatus = "InEscrow";

                var participant = await _context.ScheduleParticipants
    .FirstOrDefaultAsync(sp => sp.ScheduleId == scheduleId &&
                               sp.UserId == userId);

                if (participant != null && participant.Status == ParticipantStatus.PendingPayment)
                {
                    participant.Status = ParticipantStatus.Confirmed; // treat as "done payment"
                    if (!participant.JoinedDate.HasValue)
                    {
                        participant.JoinedDate = DateTime.UtcNow;
                    }

                    _context.ScheduleParticipants.Update(participant);
                }

                // 2d) Insert transaction row linked to this escrow
                var transaction = new Transaction
                {
                    WalletId = wallet.WalletId,
                    Escrow = escrow,                       // EF will set EscrowId
                    Amount = amount,
                    TransactionType = "Escrow_Hold",
                    PaymentMethod = "Wallet",
                    PaymentStatus = "Completed",
                    CreatedAt = DateTime.UtcNow,
                    PaymentCompletedAt = DateTime.UtcNow,
                    Description = $"Escrow payment ({paymentType}) for schedule {scheduleId}"
                };

                _context.Transactions.Add(transaction);

                // 2e) Save all changes
                await _context.SaveChangesAsync();
                await dbTx.CommitAsync();

                return new EscrowPaymentResult
                {
                    Success = true,
                    EscrowId = escrow.EscrowId,
                    Message = "Payment successful! Funds are held in escrow until the event is completed."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing escrow payment for schedule {ScheduleId}, user {UserId}",
                    scheduleId, userId);

                // VERY IMPORTANT: never throw here – just return failure result
                return new EscrowPaymentResult
                {
                    Success = false,
                    Message = "Failed to process payment. Please try again."
                };
            }
        }


        public async Task<object?> GetEscrowStatusAsync(int scheduleId)
        {
            var schedule = await _context.Schedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null) return null;

            return new
            {
                scheduleId = schedule.ScheduleId,
                escrowStatus = schedule.EscrowStatus,
                totalEscrowAmount = schedule.TotalEscrowAmount
            };
        }
    }
}
