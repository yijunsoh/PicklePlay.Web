using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;
using PicklePlay.Data;

namespace PicklePlay.Services
{
    public class MockPaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MockPaymentService> _logger;
        private readonly IPayPalService _payPalService;

        public MockPaymentService(ApplicationDbContext context, ILogger<MockPaymentService> logger, IPayPalService payPalService)
        {
            _context = context;
            _logger = logger;
            _payPalService = payPalService;
        }
        private DateTime GetMalaysiaTime()
        {
            var mytZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mytZone);
        }

        public async Task<PaymentResult> ProcessTopUpAsync(int userId, decimal amount, string paymentMethod, CardInfo? cardInfo = null)
        {
            try
            {
                _logger.LogInformation($"Processing top-up for user {userId}, amount: {amount}, method: {paymentMethod}");

                // Get user's wallet
                var wallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == userId);

                if (wallet == null)
                {
                    _logger.LogWarning($"Wallet not found for user {userId}");
                    return new PaymentResult { Success = false, Message = "Wallet not found" };
                }

                // Validate amount
                if (amount <= 0)
                {
                    return new PaymentResult { Success = false, Message = "Amount must be greater than 0" };
                }

                // Validate card for card payments
                if (paymentMethod == "CreditCard" || paymentMethod == "DebitCard")
                {
                    if (cardInfo == null || !IsValidCardInfo(cardInfo))
                    {
                        return new PaymentResult { Success = false, Message = "Invalid card information" };
                    }
                }

                // Mock payment processing - always success for demo
                var paymentSuccess = await ProcessMockPayment(amount, paymentMethod);
                var nowMYT = GetMalaysiaTime();

                if (paymentSuccess)
                {
                    // Update wallet balance
                    wallet.WalletBalance += amount;
                    wallet.LastUpdated = nowMYT;

                    // Create transaction record
                    var transaction = new Transaction
                    {
                        WalletId = wallet.WalletId,
                        TransactionType = "TopUp",
                        Amount = amount,
                        PaymentMethod = paymentMethod,
                        PaymentStatus = "Completed",
                        PaymentGatewayId = Guid.NewGuid().ToString(),
                        CardLastFour = cardInfo?.CardNumber.Length >= 4 ? cardInfo.CardNumber[^4..] : null,
                        PaymentCompletedAt = nowMYT,
                        CreatedAt = nowMYT,
                        Description = $"Top Up via {paymentMethod}"
                    };

                    _context.Transactions.Add(transaction);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Top-up successful for user {userId}. New balance: {wallet.WalletBalance}");

                    return new PaymentResult
                    {
                        Success = true,
                        TransactionId = transaction.TransactionId.ToString(),
                        Message = "Top-up successful!",
                        NewBalance = wallet.WalletBalance
                    };
                }
                else
                {
                    return new PaymentResult { Success = false, Message = "Payment processing failed" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing top-up for user {userId}");
                return new PaymentResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        public async Task<PaymentResult> ProcessPayPalTopUpAsync(int userId, decimal amount, string paypalPaymentId)
        {
            try
            {
                _logger.LogInformation($"Processing PayPal top-up for user {userId}, amount: {amount}, paymentId: {paypalPaymentId}");

                // Get user's wallet
                var wallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == userId);

                if (wallet == null)
                {
                    _logger.LogWarning($"Wallet not found for user {userId}");
                    return new PaymentResult { Success = false, Message = "Wallet not found" };
                }

                // Validate amount
                if (amount <= 0)
                {
                    return new PaymentResult { Success = false, Message = "Amount must be greater than 0" };
                }

                var nowMYT = GetMalaysiaTime();

                // Update wallet balance
                wallet.WalletBalance += amount;
                wallet.LastUpdated = nowMYT;

                // Create transaction record
                var transaction = new Transaction
                {
                    WalletId = wallet.WalletId,
                    TransactionType = "TopUp",
                    Amount = amount,
                    PaymentMethod = "PayPal",
                    PaymentStatus = "Completed",
                    PaymentGatewayId = paypalPaymentId,
                    PaymentCompletedAt = nowMYT,
                    CreatedAt = nowMYT,
                    Description = "Top up via PayPal"

                };

                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"PayPal top-up successful for user {userId}. New balance: {wallet.WalletBalance}");

                return new PaymentResult
                {
                    Success = true,
                    TransactionId = transaction.TransactionId.ToString(),
                    Message = "PayPal top-up successful!",
                    NewBalance = wallet.WalletBalance
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing PayPal top-up for user {userId}");
                return new PaymentResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        public async Task<PaymentResult> ProcessWithdrawAsync(int userId, decimal amount, string withdrawMethod, PaymentDetails? paymentDetails)
        {
            try
            {
                _logger.LogInformation($"Processing withdrawal for user {userId}, amount: {amount}, method: {withdrawMethod}");

                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
                if (wallet == null) return new PaymentResult { Success = false, Message = "Wallet not found" };

                if (amount <= 0) return new PaymentResult { Success = false, Message = "Amount > 0 required" };
                if (wallet.WalletBalance < amount) return new PaymentResult { Success = false, Message = "Insufficient balance" };
                if (amount < 10.00m) return new PaymentResult { Success = false, Message = "Min withdrawal RM 10.00" };

                // --- LOGIC SWITCH ---
                bool withdrawalSuccess = false;
                string gatewayId = GeneratePaymentReference();
                string notes = "";

                if (withdrawMethod == "PayPal")
                {
                    // VALIDATE EMAIL
                    if (string.IsNullOrEmpty(paymentDetails?.PayPalEmail))
                        return new PaymentResult { Success = false, Message = "PayPal email required" };

                    // CALL REAL PAYPAL PAYOUTS API
                    var batchId = await _payPalService.PayoutAsync(
                        paymentDetails.PayPalEmail,
                        amount,
                        "MYR", // Using MYR as per your previous files
                        gatewayId
                    );

                    if (!string.IsNullOrEmpty(batchId))
                    {
                        withdrawalSuccess = true;
                        gatewayId = batchId; // Store PayPal Batch ID
                        notes = $"PayPal Payout to {paymentDetails.PayPalEmail}";
                    }
                    else
                    {
                        return new PaymentResult { Success = false, Message = "PayPal Payout Failed (Check Logs)" };
                    }
                }
                else
                {
                    // Bank / Other (Keep Mock Logic)
                    await Task.Delay(1000);
                    withdrawalSuccess = true;
                    notes = $"Withdrawal to {withdrawMethod}";
                }

                var nowMYT = GetMalaysiaTime();
                if (withdrawalSuccess)
                {
                    // Update wallet balance
                    wallet.WalletBalance -= amount;
                    wallet.TotalSpent += amount;
                    wallet.LastUpdated = nowMYT;

                    var withdrawalTransaction = new Transaction
                    {
                        WalletId = wallet.WalletId,
                        TransactionType = "Withdraw",
                        Amount = -amount,
                        PaymentMethod = withdrawMethod,
                        PaymentStatus = "Completed",
                        PaymentGatewayId = gatewayId,
                        Description = $"Withdraw to {withdrawMethod}",
                        CardLastFour = GetLastFourDigits(paymentDetails),
                        CreatedAt = nowMYT,
                        PaymentCompletedAt = nowMYT,
                    };

                    _context.Transactions.Add(withdrawalTransaction);
                    await _context.SaveChangesAsync();

                    return new PaymentResult
                    {
                        Success = true,
                        Message = "Withdrawal successful",
                        TransactionId = withdrawalTransaction.TransactionId.ToString(),
                        NewBalance = wallet.WalletBalance
                    };
                }

                return new PaymentResult { Success = false, Message = "Withdrawal failed" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing withdrawal for user {userId}");
                return new PaymentResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        private string? GetLastFourDigits(PaymentDetails? paymentDetails)
        {
            if (paymentDetails == null) return null;

            if (!string.IsNullOrEmpty(paymentDetails.CardNumber))
            {
                var cleanCardNumber = paymentDetails.CardNumber.Replace(" ", "");
                return cleanCardNumber.Length >= 4 ?
                    $"{cleanCardNumber.Substring(cleanCardNumber.Length - 4)}" : null;
            }

            if (!string.IsNullOrEmpty(paymentDetails.AccountNumber))
            {
                return paymentDetails.AccountNumber.Length >= 4 ?
                    $" {paymentDetails.AccountNumber.Substring(paymentDetails.AccountNumber.Length - 4)}" : null;
            }

            if (!string.IsNullOrEmpty(paymentDetails.PayPalEmail))
            {
                var emailParts = paymentDetails.PayPalEmail.Split('@');
                return emailParts.Length == 2 ?
                    $"{emailParts[0].Substring(0, Math.Min(3, emailParts[0].Length))}•••@{emailParts[1]}" :
                    $"{paymentDetails.PayPalEmail}";
            }

            if (!string.IsNullOrEmpty(paymentDetails.BankName))
            {
                return $"{paymentDetails.BankName}";
            }

            return null;
        }

        // Add this method to simulate withdrawal processing (like your topup)
        private async Task<bool> ProcessMockWithdrawal(decimal amount, string withdrawMethod)
        {
            // Simulate API call delay (like your topup)
            await Task.Delay(1000);

            // Mock logic - always succeed for demo
            // In real scenario, integrate with actual bank/PayPal APIs
            return true;
        }

        // Helper method to generate payment reference
        private string GeneratePaymentReference()
        {
            // 1. Get Malaysia Time
            var mytZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            var nowMYT = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mytZone);

            // 2. Use nowMYT instead of UtcNow
            return $"WDW_{nowMYT:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        private async Task<bool> ProcessMockPayment(decimal amount, string paymentMethod)
        {
            // Simulate API call delay
            await Task.Delay(1000);
            return true; // Always succeed for demo
        }

        private bool IsValidCardInfo(CardInfo cardInfo)
        {
            if (string.IsNullOrEmpty(cardInfo.CardNumber) ||
                string.IsNullOrEmpty(cardInfo.ExpiryMonth) ||
                string.IsNullOrEmpty(cardInfo.ExpiryYear) ||
                string.IsNullOrEmpty(cardInfo.CVV) ||
                string.IsNullOrEmpty(cardInfo.CardHolderName))
            {
                return false;
            }

            // Simple card number validation
            var cleanNumber = cardInfo.CardNumber.Replace(" ", "");
            if (cleanNumber.Length < 13 || cleanNumber.Length > 19 || !long.TryParse(cleanNumber, out _))
            {
                return false;
            }

            // Simple expiry validation
            if (!int.TryParse(cardInfo.ExpiryMonth, out int month) || month < 1 || month > 12)
            {
                return false;
            }

            if (!int.TryParse(cardInfo.ExpiryYear, out int year) || year < DateTime.Now.Year)
            {
                return false;
            }

            // Simple CVV validation
            if (cardInfo.CVV.Length < 3 || cardInfo.CVV.Length > 4 || !int.TryParse(cardInfo.CVV, out _))
            {
                return false;
            }

            return true;
        }

        public async Task RecordFailedTransactionAsync(int userId, decimal amount, string transactionType, string paymentMethod, string reason, string? gatewayId = null)
        {
            try
            {
                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
                if (wallet == null) return; // Cannot log if no wallet exists

                var nowMYT = GetMalaysiaTime();

                var transaction = new Transaction
                {
                    WalletId = wallet.WalletId,
                    TransactionType = transactionType, // e.g., "TopUp" or "Withdraw"
                    Amount = amount,
                    PaymentMethod = paymentMethod, // e.g., "PayPal"

                    // KEY FIELDS FOR FAILURE:
                    PaymentStatus = "Failed", // Or "Cancelled"
                    Description = reason, // e.g., "User cancelled" or "Insufficient funds"

                    PaymentGatewayId = gatewayId,
                    CreatedAt = nowMYT,
                    // PaymentCompletedAt is LEFT NULL because it failed
                };

                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log failed transaction");
            }
        }
    }

    // Define DTO classes here
    public class PaymentDetails
    {
        public string? BankName { get; set; }
        public string? AccountNumber { get; set; }
        public string? AccountHolderName { get; set; }
        public string? CardNumber { get; set; }
        public string? PayPalEmail { get; set; }
    }

    public class PaymentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TransactionId { get; set; }
        public decimal NewBalance { get; set; }
    }


}