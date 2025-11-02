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
                        // REMOVED: CardBrand assignment
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

        // You can also remove the GetCardBrand method entirely since it's not used anymore

        private async Task<bool> ProcessMockPayment(decimal amount, string paymentMethod)
        {
            // Simulate API call delay
            await Task.Delay(1000);

            // Mock logic - always succeed for demo
            // In real scenario, integrate with actual payment gateways
            return true;
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
       
    }
}