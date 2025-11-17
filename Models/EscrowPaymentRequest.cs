namespace PicklePlay.Models
{
    // This matches the JSON you send from _PaymentModal.js
    public class EscrowPaymentRequest
    {
        public int ScheduleId { get; set; }
        public int UserId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentType { get; set; } = string.Empty;

        // Optional: only if your modal sends the password back to server
        public string? Password { get; set; }
    }
}
