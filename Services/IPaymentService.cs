using PicklePlay.Models;

namespace PicklePlay.Services
{
    public interface IPaymentService
    {
        Task<PaymentResult> ProcessTopUpAsync(int userId, decimal amount, string paymentMethod, CardInfo? cardInfo = null);
        Task<PaymentResult> ProcessPayPalTopUpAsync(int userId, decimal amount, string paypalPaymentId);
        
    }
    public class PaymentResult
    {
        public bool Success { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public decimal NewBalance { get; set; }
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