using System.Threading.Tasks;

namespace PicklePlay.Services
{
    public interface IEscrowService
    {
        Task<bool> CanUserAffordPaymentAsync(int userId, decimal amount);

        Task<EscrowPaymentResult> ProcessPaymentAsync(
            int scheduleId,
            int userId,
            decimal amount,
            string paymentType);

        Task<object?> GetEscrowStatusAsync(int scheduleId);
    }

    public class EscrowPaymentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? EscrowId { get; set; }
    }
}
