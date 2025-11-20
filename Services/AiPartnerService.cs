using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PicklePlay.Services
{
    public class AiPartnerService : IAiPartnerService
    {
        private readonly ApplicationDbContext _context;

        // REVISED Weights (Total ~ 1.0)
        private readonly double wRating = 0.20;      // Skill level
        private readonly double wHistory = 0.15;     // Past chemistry
        private readonly double wVibe = 0.15;        // NEW: Endorsement/Vibe matching
        private readonly double wLocation = 0.15;    // Proximity
        private readonly double wActiveness = 0.15;  // Availability
        private readonly double wAgeGender = 0.20;   // Demographics

        public AiPartnerService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<AiSuggestionViewModel>> SuggestPartnersAsync(string requestingUserId, int maxSuggestions = 5)
        {
            // 1. Fetch Users WITH Rank
            var allUsers = await _context.Users
                                         .AsNoTracking()
                                         .Include(u => u.Rank)
                                         .ToListAsync();

            if (allUsers == null || !allUsers.Any()) return new List<AiSuggestionViewModel>();

            // 2. Identify Requester (Strict Check)
            var requester = allUsers.FirstOrDefault(u =>
                u.UserId.ToString() == requestingUserId ||
                u.Username.Equals(requestingUserId, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Equals(requestingUserId, StringComparison.OrdinalIgnoreCase)
            );

            if (requester == null) return new List<AiSuggestionViewModel>();

            // 3. DATA PRE-FETCHING (Performance Optimization)
            
            // A. Fetch History
            var pastPartners = await _context.Set<RankMatchHistory>()
                                             .AsNoTracking()
                                             .Where(h => h.UserId == requester.UserId && h.PartnerId != null)
                                             .Select(h => h.PartnerId!.Value)
                                             .ToListAsync();
            
            var partnerCounts = pastPartners.GroupBy(id => id)
                                            .ToDictionary(g => g.Key, g => g.Count());

            // B. Fetch Endorsements (Grouped by Receiver)
            // We fetch all endorsements to compare traits locally
            var allEndorsements = await _context.Set<Endorsement>()
                                                .AsNoTracking()
                                                .Select(e => new { e.ReceiverUserId, e.Personality, e.Skill })
                                                .ToListAsync();

            var endorsementLookup = allEndorsements.GroupBy(e => e.ReceiverUserId)
                                                   .ToDictionary(g => g.Key, g => g.ToList());

            // Analyze Requester's Vibe (Top traits)
            var myTraits = new List<string>();
            if (endorsementLookup.ContainsKey(requester.UserId))
            {
                var myEndorsements = endorsementLookup[requester.UserId];
                // Get most common Personality (excluding None)
                var topPersonality = myEndorsements
                    .Where(e => e.Personality != PersonalityEndorsement.None)
                    .GroupBy(e => e.Personality)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();
                
                // Get most common Skill (excluding None)
                var topSkill = myEndorsements
                    .Where(e => e.Skill != SkillEndorsement.None)
                    .GroupBy(e => e.Skill)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                if (topPersonality != PersonalityEndorsement.None) myTraits.Add(topPersonality.ToString());
                if (topSkill != SkillEndorsement.None) myTraits.Add(topSkill.ToString());
            }


            var candidates = new List<AiSuggestionViewModel>();

            foreach (var u in allUsers)
            {
                // Exclude self
                if (u.UserId == requester.UserId) continue;

                // --- Feature Extraction ---

                // 1. RATING
                double ratingVal = (double)(u.Rank?.Rating ?? 0m); 
                double scoreRating = Math.Min(1.0, ratingVal / 5.0);

                // 2. HISTORY
                int timesPartnered = partnerCounts.ContainsKey(u.UserId) ? partnerCounts[u.UserId] : 0;
                double scoreHistory = timesPartnered == 0 ? 0.0 : (timesPartnered >= 3 ? 1.0 : 0.5);

                // 3. ENDORSEMENT MATCHING (The Vibe Check)
                double scoreVibe = 0.5; // Neutral start
                string sharedTrait = "";
                
                if (endorsementLookup.ContainsKey(u.UserId) && myTraits.Any())
                {
                    var theirEndorsements = endorsementLookup[u.UserId];
                    
                    // Check if they have any of MY top traits
                    var matchPersonality = theirEndorsements.Any(e => e.Personality.ToString() == myTraits.FirstOrDefault(t => Enum.IsDefined(typeof(PersonalityEndorsement), t)));
                    var matchSkill = theirEndorsements.Any(e => e.Skill.ToString() == myTraits.FirstOrDefault(t => Enum.IsDefined(typeof(SkillEndorsement), t)));

                    if (matchPersonality && matchSkill) {
                        scoreVibe = 1.0; // Strong match
                        sharedTrait = myTraits.FirstOrDefault(t => Enum.IsDefined(typeof(PersonalityEndorsement), t))!; // Prefer showing personality
                    }
                    else if (matchPersonality || matchSkill) {
                        scoreVibe = 0.8; // Good match
                        // Find which one matched for the explanation
                        if (matchPersonality) 
                             sharedTrait = theirEndorsements.First(e => myTraits.Contains(e.Personality.ToString())).Personality.ToString();
                        else 
                             sharedTrait = theirEndorsements.First(e => myTraits.Contains(e.Skill.ToString())).Skill.ToString();
                    }
                }

                // 4. LOCATION & BASICS
                double scoreLocation = ComputeLocationScore(requester, u.Location ?? "");
                double activeness = ComputeActiveness(u);
                double scoreAgeGender = ComputeAgeGenderScore(requester, u.Age ?? 25, u.Gender ?? "Unspecified");
                double reliability = u.Rank != null ? (double)u.Rank.ReliabilityScore / 100.0 : 0.0;

                // --- Weighted Sum ---
                double rawScore = (scoreRating * wRating)
                                + (scoreHistory * wHistory)
                                + (scoreVibe * wVibe)
                                + (scoreLocation * wLocation)
                                + (activeness * wActiveness)
                                + (scoreAgeGender * wAgeGender);

                // Explanation Logic
                string explanation = GenerateNaturalExplanation(
                    u.Username, 
                    scoreRating, 
                    timesPartnered, 
                    sharedTrait!, // Pass the matching endorsement
                    scoreLocation, 
                    scoreAgeGender, 
                    u.Location ?? "", 
                    ratingVal
                );

                // Reliability of data
                double dataConfidence = (u.Rank != null ? 50 : 0) + (endorsementLookup.ContainsKey(u.UserId) ? 30 : 0) + 20;

                candidates.Add(new AiSuggestionViewModel
                {
                    UserId = u.UserId.ToString(),
                    UserName = u.Username,
                    ProfilePicture = u.ProfilePicture,
                    Score = Math.Round(rawScore * 100.0, 1),
                    Reliability = Math.Min(100, dataConfidence),
                    SuitabilityLabel = LabelFromScore(rawScore),
                    Explanation = explanation
                });
            }

            var ordered = candidates.OrderByDescending(c => c.Score).Take(maxSuggestions).ToList();

            foreach (var s in ordered)
            {
                await LogSuggestionAsync(requester.UserId.ToString(), s);
            }

            return ordered;
        }

        public async Task LogSuggestionAsync(string requestingUserId, AiSuggestionViewModel suggestion)
        {
            var rec = new AiSuggestedPartner
            {
                RequestedByUserId = requestingUserId,
                SuggestedUserId = suggestion.UserId,
                Score = suggestion.Score,
                FeaturesJson = JsonConvert.SerializeObject(new { suggestion.SuitabilityLabel, suggestion.Explanation }),
                ReliabilityEstimate = suggestion.Reliability,
                CreatedAt = DateTime.UtcNow
            };
            _context.AiSuggestedPartners.Add(rec);
            await _context.SaveChangesAsync();
        }

        #region Helpers

        private string GenerateNaturalExplanation(string name, double sRating, int timesPartnered, string sharedTrait, double sLoc, double sDemo, string locName, double actualRating)
        {
            var sb = new StringBuilder();

            // Priority 1: History
            if (timesPartnered > 0)
                sb.Append($"Strong chemistry! You've played with {name} {timesPartnered} times. ");

            // Priority 2: Endorsement/Vibe Match (NEW)
            if (!string.IsNullOrEmpty(sharedTrait))
                sb.Append($"You are both recognized for being {sharedTrait}. ");

            // Priority 3: Skill
            if (actualRating >= 4.0) sb.Append($"Pro-level skills ({actualRating:0.0}). ");
            else if (actualRating >= 3.0) sb.Append($"Solid skills ({actualRating:0.0}). ");

            // Priority 4: Location
            if (sLoc >= 0.9) sb.Append($"Located nearby in {locName}. ");
            
            // Priority 5: Demo
            if (sDemo >= 0.7) sb.Append("Matches your demographic preferences. ");

            if (sb.Length == 0) sb.Append("A balanced match based on general profile data.");

            return sb.ToString().Trim();
        }

        private double ComputeActiveness(User user)
        {
            if (!user.LastLogin.HasValue) return 0.5;
            var days = (DateTime.UtcNow - user.LastLogin.Value).TotalDays;
            return Math.Max(0.0, Math.Min(1.0, 1.0 - (days / 30.0)));
        }

        private double ComputeLocationScore(User requester, string candidateLocation)
        {
            if (string.IsNullOrEmpty(requester.Location) || string.IsNullOrEmpty(candidateLocation)) return 0.5;
            return requester.Location.Equals(candidateLocation, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.4;
        }

        private double ComputeAgeGenderScore(User requester, int candidateAge, string candidateGender)
        {
            int reqAge = requester.Age ?? -1;
            double score = 0.5;
            if (reqAge > 0 && candidateAge > 0)
            {
                int diff = Math.Abs(reqAge - candidateAge);
                if (diff <= 5) score += 0.3;
                else if (diff <= 10) score += 0.1;
                else score -= 0.1;
            }
            return Math.Max(0.0, Math.Min(1.0, score));
        }

        private static string LabelFromScore(double s)
        {
            if (s >= 0.85) return "Perfect Match";
            if (s >= 0.70) return "Excellent";
            if (s >= 0.50) return "Good";
            return "Potential";
        }
        #endregion
    }
}