using System;

namespace PicklePlay.Models
{
    public class AiSuggestedPartner
    {
        public int Id { get; set; }
        public string RequestedByUserId { get; set; } = null!;
        public string SuggestedUserId { get; set; } = null!;
        public double Score { get; set; }
        public string FeaturesJson { get; set; } = "{}";
        public double ReliabilityEstimate { get; set; } = 0.0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}