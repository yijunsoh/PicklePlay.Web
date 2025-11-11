using System;
using System.ComponentModel.DataAnnotations;

namespace PicklePlay.ViewModels
{
    public class TransactionLogViewModel
    {
        public int TransactionId { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string TransactionType { get; set; } = null!;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = null!;
        public string PaymentStatus { get; set; } = null!;
        public string? PaymentGatewayId { get; set; }
        public string? CardLastFour { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaymentCompletedAt { get; set; }
        public int WalletId { get; set; }
        public int? EscrowId { get; set; }
        public bool IsEscrowRelated { get; set; }
    }

    public class TransactionFilterViewModel
    {
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? TransactionType { get; set; }
        public string? PaymentStatus { get; set; }
        public string? PaymentMethod { get; set; }
    }

    public class TransactionSummaryViewModel
    {
        public int TotalTransactions { get; set; }
        public decimal TotalAmount { get; set; }
        public int SuccessfulTransactions { get; set; }
        public int PendingTransactions { get; set; }
        public int FailedTransactions { get; set; }
    }

    public class TransactionLogMainViewModel
    {
        public List<TransactionLogViewModel> Transactions { get; set; } = new();
        public TransactionFilterViewModel Filter { get; set; } = new();
        public TransactionSummaryViewModel Summary { get; set; } = new();
    }

    public class UpdateTransactionStatusRequest
    {
        public int TransactionId { get; set; }
        public string NewStatus { get; set; } = null!;
        public string? AdminNote { get; set; }
    }
}