using System;

namespace PicklePlay.Models.ViewModels
{
    public class AiSuggestionViewModel
    {
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = "Unknown";
        
        public string? ProfilePicture { get; set; } 
        public double Score { get; set; }
        public string SuitabilityLabel { get; set; } = "Good";
        public string Explanation { get; set; } = string.Empty;
        public double Reliability { get; set; }
        public Dictionary<string, double> ScoreBreakdown { get; set; } = new();
    }
}