using System;
using System.Collections.Generic;

namespace PicklePlay.ViewModels
{
    public class CommunityAdminDashboardViewModel
    {
        // Community basics
        public int CommunityId { get; set; }
        public string CommunityName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? CommunityLocation { get; set; }
        public string CommunityType { get; set; } = "Public";
        public string Status { get; set; } = "Active";
        public DateTime CreatedDate { get; set; }
        public string? CommunityPic { get; set; }

        // Creator
        public int CreatedByUserId { get; set; }
        public string CreatedByUserName { get; set; } = string.Empty;

        // Counts
        public int MemberCountActive { get; set; }
        public int MemberCountTotal { get; set; }
        public int AdminCount { get; set; }
        public int ModeratorCount { get; set; }
        public int BlockedUserCount { get; set; }
        public int AnnouncementCount { get; set; }

        // Lists
        public List<AnnouncementItem> LatestAnnouncements { get; set; } = new();
        public List<MemberItem> Members { get; set; } = new();           // full list for table
        public List<JoinRequestItem> JoinRequests { get; set; } = new(); // pending requests
        public List<MemberItem> LatestMembers { get; set; } = new();     // optional small card

        // Viewer role (shown in header instead of ViewBag)
        public string CurrentUserRole { get; set; } = "Member";

        // --- Nested item models ---
        public class AnnouncementItem
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public DateTime PostDate { get; set; }
            public int PosterUserId { get; set; }
            public string PosterName { get; set; } = string.Empty;
        }

        public class MemberItem
        {
            public int UserId { get; set; }
            public string UserName { get; set; } = string.Empty; // from User.FullName (or Username)
            public string Role { get; set; } = "Member";          // CommunityMember.CommunityRole
            public string Status { get; set; } = "Active";        // CommunityMember.Status
            public DateTime JoinDate { get; set; }
        }

        public class JoinRequestItem
        {
            // If requests live in CommunityMember with Status="Pending",
            // map MemberId -> RequestId, JoinDate -> RequestedDate.
            public int RequestId { get; set; }
            public int UserId { get; set; }
            public string Username { get; set; } = string.Empty;
            public DateTime RequestedDate { get; set; }
        }
    }
}
