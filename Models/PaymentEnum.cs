namespace PicklePlay.Models
{
    // For TOP-UP (adding money to wallet)
    public enum TopUpMethod
    {
        CreditCard,
        DebitCard, 
        PayPal
    }

    // For WITHDRAWAL (taking money out of wallet)
    public enum WithdrawalMethod
    {
        BankTransfer,
        PayPal,
        DebitCard
    }

    public enum PaymentStatus
    {
        Pending,
        Completed,
        Failed
    }

    // Add this enum for transaction types
    public enum TransactionType
    {
        TopUp,
        Withdraw,
        Payment,
        Refund
    }
}