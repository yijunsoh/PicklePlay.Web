using System.Collections.Generic;
using PicklePlay.Models;

namespace PicklePlay.ViewModels
{
    public class WalletManagementViewModel
    {
        public Wallet? Wallet { get; set; }
        public User? User { get; set; }
        
        public List<Transaction>? Transactions { get; set; }
        public List<Escrow>? Escrows { get; set; }
        public List<EscrowDispute>? EscrowDisputes { get; set; }
        public List<EscrowPaymentRequest>? EscrowPaymentRequests { get; set; }
    }
}
