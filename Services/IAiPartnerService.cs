using System.Collections.Generic;
using System.Threading.Tasks;
using PicklePlay.Models.ViewModels;

namespace PicklePlay.Services
{
    public interface IAiPartnerService
    {
        Task<IReadOnlyList<AiSuggestionViewModel>> SuggestPartnersAsync(string requestingUserId, int maxSuggestions = 5);
        Task LogSuggestionAsync(string requestingUserId, AiSuggestionViewModel suggestion);
    }
}