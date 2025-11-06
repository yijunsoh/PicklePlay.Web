using System.Collections.Generic;
using PicklePlay.Models;

namespace PicklePlay.Models.ViewModels
{
    public class CommunicationHubViewModel
    {
        public List<TeamInvitation> PendingTeamInvitations { get; set; } = new List<TeamInvitation>();
        public List<Friendship> PendingFriendRequests { get; set; } = new List<Friendship>();
        public List<Friendship> Friends { get; set; } = new List<Friendship>();
        public List<CommunityInvitation> PendingCommunityInvitations { get; set; } = new List<CommunityInvitation>();
    }
}
