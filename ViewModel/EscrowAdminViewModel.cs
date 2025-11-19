using System;

namespace PicklePlay.Models.ViewModels
{
    public class EscrowAdminViewModel
    {
        public int EscrowId { get; set; }

        // Schedule
        public int ScheduleId { get; set; }
        public string GameTitle { get; set; } = string.Empty;
        public DateTime? GameEndTime { get; set; }
        public decimal TotalEscrowAmount { get; set; }

        // Host Info
        public int? HostUserId { get; set; }
        public string HostName { get; set; } = string.Empty;

        // Payer Info (UserId inside Escrow)
        public int PayerUserId { get; set; }
        public string PayerName { get; set; } = string.Empty;

        // Escrow Status
        public string Status { get; set; } = string.Empty;

        // Amount (sum of all transactions for this escrow)
        public decimal Amount { get; set; }

        // Player Count (confirmed participants count)
        public int PlayerCount { get; set; }

        // Dispute
        public string? DisputeReason { get; set; }
    }
}
