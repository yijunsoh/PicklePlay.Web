using System;
using System.Collections.Generic;

namespace PicklePlay.ViewModels
{
    public class GrowthReportViewModel
    {
        public string ReportTitle { get; set; } = "Yearly User Growth (2025)";
        public List<MonthlyGrowthData> MonthlyData { get; set; } = new List<MonthlyGrowthData>();
        public ReportSummary Summary { get; set; } = new ReportSummary();
    }

    public class MonthlyGrowthData
    {
        public string Month { get; set; } = string.Empty;
        public int Users { get; set; }
        public string Color { get; set; } = "#4CAF50";
    }

    public class ReportSummary
    {
        public string PeakGrowthMonth { get; set; } = string.Empty;
        public int PeakGrowthValue { get; set; }
        public string LowestGrowthMonth { get; set; } = string.Empty;
        public int LowestGrowthValue { get; set; }
        public int TotalUsersGained { get; set; }
        public int AverageMonthlyGrowth { get; set; }
        public string Period { get; set; } = "6 months";
    }
}