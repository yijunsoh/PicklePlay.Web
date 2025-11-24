using PicklePlay.Models;

namespace PicklePlay.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int ActiveCommunities { get; set; }
        public int TotalPendingRequests { get; set; } // Combined total of all pending requests
        public decimal TotalTransactions { get; set; }
        
        // Individual pending counts for the stats card
        public int PendingCommunityRequestsCount { get; set; }
        public int PendingEscrowDisputesCount { get; set; }
        public int PendingRefundRequestsCount { get; set; }
        public int PendingSuspensionRequestsCount { get; set; }
        
        // Recent activity
        public List<RecentActivityViewModel> RecentActivities { get; set; } = new();
        
        // Chart data - simplified back to just new users
        public List<MonthlyUserGrowth> MonthlyUserGrowth { get; set; } = new();
        
        // Total users by month for tooltip
        public Dictionary<string, int> MonthlyTotalUsers { get; set; } = new();
    }

    public class RecentActivityViewModel
    {
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class MonthlyUserGrowth
    {
        public string Month { get; set; } = string.Empty;
        public int NewUsers { get; set; }
    }
}