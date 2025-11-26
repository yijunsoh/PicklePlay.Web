using PicklePlay.Models;
using static PicklePlay.Services.MockPaymentService;

namespace PicklePlay.Services
{
    public interface IPaymentService
    {
        Task<PaymentResult> ProcessTopUpAsync(int userId, decimal amount, string paymentMethod, CardInfo? cardInfo = null);
        Task<PaymentResult> ProcessPayPalTopUpAsync(int userId, decimal amount, string paypalPaymentId);
        Task<PaymentResult> ProcessWithdrawAsync(int userId, decimal amount, string withdrawMethod, PaymentDetails? paymentDetails);
        Task RecordFailedTransactionAsync(int userId, decimal amount, string transactionType, string paymentMethod, string reason, string? gatewayId = null);
    }
   

    public class CardInfo
    {
        public string CardNumber { get; set; } = string.Empty;
        public string ExpiryMonth { get; set; } = string.Empty;
        public string ExpiryYear { get; set; } = string.Empty;
        public string CVV { get; set; } = string.Empty;
        public string CardHolderName { get; set; } = string.Empty;
    }
}