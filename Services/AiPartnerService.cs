using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OpenAI.Chat; 
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.Services
{
    public class AiPartnerService : IAiPartnerService
    {
        private readonly ApplicationDbContext _context;
        
        public AiPartnerService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<AiSuggestionViewModel>> SuggestPartnersAsync(string requestingUserId, int maxSuggestions = 5)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("OpenAI API Key is missing.");
                return new List<AiSuggestionViewModel>();
            }

            // 1. Fetch Data
            var allUsers = await _context.Users
                                         .AsNoTracking()
                                         .Include(u => u.Rank)
                                         .ToListAsync();

            var requester = allUsers.FirstOrDefault(u =>
                u.UserId.ToString() == requestingUserId ||
                u.Username.Equals(requestingUserId, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Equals(requestingUserId, StringComparison.OrdinalIgnoreCase)
            );

            if (requester == null) return new List<AiSuggestionViewModel>();

            // Fetch History (IDs of people the requester has played with)
            var pastPartnersIds = await _context.Set<RankMatchHistory>()
                                             .AsNoTracking()
                                             .Where(h => h.UserId == requester.UserId && h.PartnerId != null)
                                             .Select(h => h.PartnerId!.Value)
                                             .ToListAsync();
            
            // Fetch Endorsements
            var allEndorsements = await _context.Set<Endorsement>()
                                                .AsNoTracking()
                                                .Select(e => new { e.ReceiverUserId, e.Personality, e.Skill })
                                                .ToListAsync();

            var endorsementLookup = allEndorsements.GroupBy(e => e.ReceiverUserId)
                                                   .ToDictionary(g => g.Key, g => (dynamic)g.ToList());

            // 2. Build Profiles for AI
            // Requester profile doesn't need match count (it's relative to candidates)
            var requesterProfile = BuildUserProfile(requester, 0, endorsementLookup);
            
            var candidates = allUsers
                .Where(u => u.UserId != requester.UserId)
                .Select(u => 
                {
                    // ⬇️ FIX: Calculate specific history match count for this candidate
                    int timesPlayed = pastPartnersIds.Count(id => id == u.UserId);
                    return BuildUserProfile(u, timesPlayed, endorsementLookup);
                })
                .ToList();

            // Optimization: Filter top candidates to save tokens
            // We prioritize those who have played before OR have high rank/endorsements
            var candidateBatch = candidates
                .OrderByDescending(c => ((dynamic)c).HistoryWithRequester.Contains("Played") ? 1 : 0) // Prioritize history
                .Take(30)
                .ToList(); 

            if (!candidateBatch.Any()) return new List<AiSuggestionViewModel>();

            // 3. Call OpenAI
            try
            {
                var aiResults = await GetOpenAiAnalysis(apiKey, requesterProfile, candidateBatch);

                var finalSuggestions = new List<AiSuggestionViewModel>();

                foreach (var result in aiResults)
                {
                    var userObj = allUsers.FirstOrDefault(u => u.UserId == result.UserId);
                    if (userObj == null) continue;

                    finalSuggestions.Add(new AiSuggestionViewModel
                    {
                        UserId = userObj.UserId.ToString(),
                        UserName = userObj.Username,
                        ProfilePicture = userObj.ProfilePicture,
                        Score = result.TotalScore, 
                        SuitabilityLabel = result.Label, 
                        Explanation = result.Explanation,
                        ScoreBreakdown = result.Breakdown ?? new Dictionary<string, double>(),
                        Reliability = userObj.Rank != null ? 90 : 60 
                    });
                }

                var ordered = finalSuggestions.OrderByDescending(c => c.Score).Take(maxSuggestions).ToList();

                foreach (var s in ordered)
                {
                    await LogSuggestionAsync(requester.UserId.ToString(), s);
                }

                return ordered;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenAI Error: {ex.Message}");
                return new List<AiSuggestionViewModel>();
            }
        }

        public async Task LogSuggestionAsync(string requestingUserId, AiSuggestionViewModel suggestion)
        {
            var rec = new AiSuggestedPartner
            {
                RequestedByUserId = requestingUserId,
                SuggestedUserId = suggestion.UserId,
                Score = suggestion.Score,
                FeaturesJson = JsonConvert.SerializeObject(new { suggestion.SuitabilityLabel, suggestion.Explanation, suggestion.ScoreBreakdown }),
                ReliabilityEstimate = suggestion.Reliability,
                CreatedAt = DateTime.UtcNow
            };
            _context.AiSuggestedPartners.Add(rec);
            await _context.SaveChangesAsync();
        }

        // --- PRIVATE HELPERS ---

        // ⬇️ FIX: Added matchCount parameter
        private object BuildUserProfile(User u, int matchCount, Dictionary<int, dynamic> endorsements)
        {
            var traits = new List<string>();
            if (endorsements.ContainsKey(u.UserId))
            {
                var userEndorsements = endorsements[u.UserId];
                if (userEndorsements.Count > 0) traits.Add($"Has {userEndorsements.Count} endorsements");
                // Add specific traits if available in your dynamic object
                // foreach(var e in userEndorsements) { ... }
            }

            string activityLevel = "Inactive";
            if (u.LastLogin.HasValue)
            {
                var days = (DateTime.UtcNow - u.LastLogin.Value).TotalDays;
                if (days < 7) activityLevel = "Very Active";
                else if (days < 30) activityLevel = "Active";
                else activityLevel = "Occasional";
            }

            // ⬇️ FIX: Explicitly state the history relationship
            string historyStr = matchCount > 0 
                ? $"Played together {matchCount} times (Strong Chemistry)" 
                : "No recorded matches together";

            return new
            {
                Id = u.UserId,
                Name = u.Username,
                Rating = u.Rank?.Rating ?? 0,
                Location = u.Location ?? "Unknown",
                Age = u.Age ?? 25,
                Gender = u.Gender ?? "Unspecified",
                Activity = activityLevel,
                Traits = traits,
                HistoryWithRequester = historyStr // ⬅️ The AI will now see this clearly
            };
        }

        private async Task<List<AiResultDto>> GetOpenAiAnalysis(string apiKey, object requesterProfile, List<object> candidates)
        {
            ChatClient client = new(model: "gpt-4o-mini", apiKey: apiKey);

            string requesterJson = JsonConvert.SerializeObject(requesterProfile);
            string candidatesJson = JsonConvert.SerializeObject(candidates);

            // ⬇️ UPDATED: Nuanced Scoring Logic to prevent "Zero Scores"
            string systemPrompt = @"
You are an elite Pickleball Matchmaking Expert. 
Calculate a compatibility score (0-100) for the CURRENT USER against each CANDIDATE.
Do NOT give 0 points unless data is completely missing. Use tiered scoring.

SCORING GUIDELINES (Total 100%):

1. Rating (Max 20): Skill Compatibility
   - Exact match or diff < 0.25: Score 20/20.
   - Diff < 0.5: Score 15-18.
   - Diff < 1.0: Score 10-14.
   - Diff > 1.0 but both are rated: Score 5-9 (Do NOT give 0 just for being different levels).
   - Unrated/Unknown: Score 2.

2. History (Max 20): Chemistry
   - Played > 3 times: Score 20.
   - Played 1-2 times: Score 12-18.
   - No history: Score 0 (This is fair, history is a bonus).

3. Vibe/Endorsements (Max 15): Personality
   - High overlap in traits: Score 15.
   - Some overlap OR both have 'Sportsmanship/Friendly': Score 8-12.
   - No overlap but candidate has endorsements: Score 5 (Good community standing).
   - No endorsements: Score 0.

4. Location (Max 15): Logistics
   - Exact location match: Score 15.
   - Different location but both known: Score 5-10 (Assume travel is possible).
   - Unknown location: Score 0.

5. Activeness (Max 15): Availability
   - 'Very Active': 15.
   - 'Active': 10.
   - 'Occasional': 5.
   - 'Inactive': 0.

6. Age/Gender (Max 15): Demographics
   - Perfect fit (e.g., similar age group): 15.
   - Moderate fit (e.g., 10-15 year gap): 8-12.
   - Large gap but legal adults: 5.

OUTPUT FORMAT:
Return a strictly valid JSON array.
[
  {
    ""UserId"": 123,
    ""TotalScore"": 88.5,
    ""Label"": ""Perfect Match"",
    ""Explanation"": ""Detailed, persuasive reason. Mention specific details like 'Although your ratings differ slightly (3.5 vs 4.0), your high activeness makes this a good potential match'."",
    ""Breakdown"": {
        ""Rating"": 18,
        ""History"": 0,
        ""Vibe"": 12,
        ""Location"": 10,
        ""Activeness"": 15,
        ""Demographics"": 13
    }
  }
]
";

            string userPrompt = $@"
CURRENT USER: 
{requesterJson}

CANDIDATES: 
{candidatesJson}

Analyze and rank them.
";

            ChatCompletion completion = await client.CompleteChatAsync(
                new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt)
                }
            );

            string responseText = completion.Content[0].Text;
            responseText = responseText.Replace("```json", "").Replace("```", "").Trim();

            return JsonConvert.DeserializeObject<List<AiResultDto>>(responseText) ?? new List<AiResultDto>();
        }

        private class AiResultDto
        {
            public int UserId { get; set; }
            public double TotalScore { get; set; }
            public string Label { get; set; } = "";
            public string Explanation { get; set; } = "";
            public Dictionary<string, double>? Breakdown { get; set; }
        }
    }
}


