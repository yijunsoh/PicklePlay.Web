using System;
using System.Collections.Generic;

namespace PicklePlay.ViewModels
{
    public class OperationalReportViewModel
    {
        // Header Info
        public string ReportTitle { get; set; } = "Daily Cash Flow & Operations";
        public string CompanyName { get; set; } = "PicklePlay HQ";
        public string CompanyAddress { get; set; } = "Jalan Genting Kelang, 53300 Setapak, Kuala Lumpur, Malaysia";
        public string CompanyPhone { get; set; } = "+60 18-266 3966";
        public string LogoPath { get; set; } = "/images/logoV3.png";

        // Criteria
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        // The Data
        public List<DailyMetric> DailyMetrics { get; set; } = new List<DailyMetric>();

        // Grand Totals
        public decimal TotalDeposits { get; set; }
        public decimal TotalWithdrawals { get; set; }
        public decimal TotalEscrowLocked { get; set; }   // "Escrow Hold"
        public decimal TotalEscrowReleased { get; set; } // "Escrow Released"
        public decimal TotalRefunds { get; set; }
        
        // Net Flow = (Money In) - (Money Out)
        public decimal NetCashFlow => TotalDeposits - TotalWithdrawals;
    }

    public class DailyMetric
    {
        public DateTime Date { get; set; }
        public decimal Deposits { get; set; }       // TopUp
        public decimal Withdrawals { get; set; }    // Withdraw
        public decimal EscrowLocked { get; set; }   // Escrow Hold (Money committed)
        public decimal EscrowReleased { get; set; } // Escrow Released (Money paid to winner)
        public decimal Refunds { get; set; }        // Refund
    }
}