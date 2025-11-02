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
        public async Task<PaymentResult> ProcessWithdrawAsync(int userId, decimal amount, WithdrawalMethod withdrawMethod, PaymentDetails? paymentDetails = null)
        {
            try
            {
                _logger.LogInformation($"Processing withdraw for user {userId}, amount: {amount}, method: {withdrawMethod}");

                // Get user's wallet
                var wallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserId == userId);

                _logger.LogInformation($"Wallet retrieved - IsNull: {wallet == null}, Balance: {wallet?.WalletBalance}");

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

                // Check sufficient balance
                if (wallet.WalletBalance < amount)
                {
                    return new PaymentResult { Success = false, Message = "Insufficient balance" };
                }

                // Validate minimum withdraw amount
                if (amount < 10)
                {
                    return new PaymentResult { Success = false, Message = "Minimum withdraw amount is RM 10" };
                }

                // Validate payment details based on method
                string? validationError = ValidatePaymentDetails(withdrawMethod, paymentDetails);
                if (validationError != null)
                {
                    return new PaymentResult { Success = false, Message = validationError };
                }

                // Use consistent fee for all methods
                decimal withdrawalFee = 1.00m;

                decimal totalDeducted = amount + withdrawalFee;

                // Check if balance can cover amount + fee
                if (wallet.WalletBalance < totalDeducted)
                {
                    return new PaymentResult
                    {
                        Success = false,
                        Message = $"Insufficient balance to cover withdrawal amount and fee (RM {withdrawalFee})"
                    };
                }

                _logger.LogInformation($"Balance check passed - Current: {wallet.WalletBalance}, Deducting: {totalDeducted}");

                // Process withdrawal based on method
                bool withdrawSuccess = await ProcessWithdrawalByMethod(amount, withdrawMethod, paymentDetails);

                if (withdrawSuccess)
                {
                    // Update wallet balance (deduct amount + fee)
                    wallet.WalletBalance -= totalDeducted;
                    wallet.LastUpdated = DateTime.UtcNow;

                    _logger.LogInformation($"Wallet balance updated to: {wallet.WalletBalance}");

                    // Create transaction record for the withdrawal
                    var transaction = new Transaction
                    {
                        WalletId = wallet.WalletId,
                        TransactionType = "Withdraw",
                        Amount = -amount,
                        PaymentMethod = withdrawMethod.ToString(),
                        PaymentStatus = "Completed",
                        PaymentGatewayId = Guid.NewGuid().ToString(),
                        CardLastFour = withdrawMethod == WithdrawalMethod.DebitCard && paymentDetails?.CardNumber != null ?
                                      paymentDetails.CardNumber.Length >= 4 ? paymentDetails.CardNumber[^4..] : null : null,
                        PaymentCompletedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };

                    _logger.LogInformation($"Creating transaction: WalletId={transaction.WalletId}, Amount={transaction.Amount}");

                    _context.Transactions.Add(transaction);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Withdraw successful for user {userId}. Amount: {amount}, Fee: {withdrawalFee}, New balance: {wallet.WalletBalance}");

                    return new PaymentResult
                    {
                        Success = true,
                        TransactionId = transaction.TransactionId.ToString(),
                        Message = $"Withdrawal successful! RM {withdrawalFee} fee applied.",
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
                _logger.LogError(ex, $"Error processing withdraw for user {userId}");
                return new PaymentResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        private string? ValidatePaymentDetails(WithdrawalMethod method, PaymentDetails? details)
        {
            if (details == null)
            {
                return "Payment details are required";
            }

            switch (method)
            {
                case WithdrawalMethod.BankTransfer:
                    if (string.IsNullOrEmpty(details.BankName) ||
                        string.IsNullOrEmpty(details.AccountNumber) ||
                        string.IsNullOrEmpty(details.AccountHolderName))
                        return "Bank name, account number, and account holder name are required for bank transfer";
                    break;

                case WithdrawalMethod.PayPal:
                    if (string.IsNullOrEmpty(details.PayPalEmail))
                        return "PayPal email is required";
                    if (!IsValidEmail(details.PayPalEmail))
                        return "Invalid PayPal email format";
                    break;

                case WithdrawalMethod.DebitCard:
                    if (string.IsNullOrEmpty(details.CardNumber))
                        return "Card number is required for debit card withdrawal";
                    if (!IsValidCardNumber(details.CardNumber))
                        return "Invalid card number";
                    break;
            }

            return null;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidCardNumber(string cardNumber)
        {
            var cleanNumber = cardNumber.Replace(" ", "");
            return cleanNumber.Length >= 13 && cleanNumber.Length <= 19 && long.TryParse(cleanNumber, out _);
        }

        private async Task<bool> ProcessWithdrawalByMethod(decimal amount, WithdrawalMethod method, PaymentDetails? details)
        {
            // Simulate API call delay
            await Task.Delay(1500);

            // Mock processing based on method
            switch (method)
            {
                case WithdrawalMethod.BankTransfer:
                    _logger.LogInformation($"Processing bank transfer to {details?.BankName} account {details?.AccountNumber}");
                    // In real scenario: Call bank API
                    break;

                case WithdrawalMethod.PayPal:
                    _logger.LogInformation($"Processing PayPal payout to {details?.PayPalEmail}");
                    // In real scenario: Call PayPal Payouts API
                    // await _payPalService.CreatePayoutAsync(details.PayPalEmail, amount);
                    break;

                case WithdrawalMethod.DebitCard:
                    _logger.LogInformation($"Processing debit card refund to card ending with {details?.CardNumber?[^4..]}");
                    // In real scenario: Call card processor API
                    break;
            }

            // Mock - always success for demo
            return true;
        }
    }
}