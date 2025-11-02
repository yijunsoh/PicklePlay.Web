using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;
using PicklePlay.Data;

namespace PicklePlay.Services
{
    public class MockPaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MockPaymentService> _logger;

        public MockPaymentService(ApplicationDbContext context, ILogger<MockPaymentService> logger)
        {
            _context = context;
            _logger = logger;
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

                if (paymentSuccess)
                {
                    // Update wallet balance
                    wallet.WalletBalance += amount;
                    wallet.LastUpdated = DateTime.UtcNow;

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
                        PaymentCompletedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
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

                // Update wallet balance
                wallet.WalletBalance += amount;
                wallet.LastUpdated = DateTime.UtcNow;

                // Create transaction record
                var transaction = new Transaction
                {
                    WalletId = wallet.WalletId,
                    TransactionType = "TopUp",
                    Amount = amount,
                    PaymentMethod = "PayPal",
                    PaymentStatus = "Completed",
                    PaymentGatewayId = paypalPaymentId,
                    PaymentCompletedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow

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

                // Check if user has sufficient balance
                if (wallet.WalletBalance < amount)
                {
                    return new PaymentResult { Success = false, Message = "Insufficient balance for withdrawal" };
                }

                // Validate minimum withdrawal amount
                if (amount < 10.00m)
                {
                    return new PaymentResult { Success = false, Message = "Minimum withdrawal amount is RM 10.00" };
                }

                // Mock withdrawal processing - always success for demo (like your topup)
                var withdrawalSuccess = await ProcessMockWithdrawal(amount, withdrawMethod);

                if (withdrawalSuccess)
                {
                    // Update wallet balance (REAL database update)
                    wallet.WalletBalance -= amount;
                    wallet.TotalSpent += amount;
                    wallet.LastUpdated = DateTime.UtcNow;

                    // Create withdrawal transaction record (REAL database insert)
                    var withdrawalTransaction = new Transaction
                    {
                        WalletId = wallet.WalletId,
                        TransactionType = "Withdraw",
                        Amount = -amount, // Negative amount for withdrawal
                        PaymentMethod = withdrawMethod,
                        PaymentStatus = "Completed",
                        PaymentGatewayId = GeneratePaymentReference(),
                        CardLastFour = GetLastFourDigits(paymentDetails),
                        CreatedAt = DateTime.UtcNow,
                        PaymentCompletedAt = DateTime.UtcNow
                    };

                    _context.Transactions.Add(withdrawalTransaction);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Withdrawal successful for user {userId}. Transaction ID: {withdrawalTransaction.TransactionId}, New Balance: {wallet.WalletBalance}");

                    return new PaymentResult
                    {
                        Success = true,
                        Message = "Withdrawal processed successfully",
                        TransactionId = withdrawalTransaction.TransactionId.ToString(),
                        NewBalance = wallet.WalletBalance
                    };
                }
                else
                {
                    return new PaymentResult { Success = false, Message = "Withdrawal processing failed" };
                }
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
            return $"WDW_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
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