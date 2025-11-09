using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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
            public string Content { get; set; } = string.Empty; // ADD THIS PROPERTY
            public DateTime PostDate { get; set; }
            public int PosterUserId { get; set; }
            public string PosterName { get; set; } = string.Empty;
        }

        public class MemberItem
        {
            public int UserId { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string Role { get; set; } = "Member";
            public string Status { get; set; } = "Active";
            public DateTime JoinDate { get; set; }
        }

        public class JoinRequestItem
        {
            public int RequestId { get; set; }
            public int UserId { get; set; }
            public string Username { get; set; } = string.Empty;
            public DateTime RequestedDate { get; set; }
        }

        // --- Create Announcement ViewModel ---
        public class CreateAnnouncementViewModel
        {
            [Required]
            public int CommunityId { get; set; }

            [Required(ErrorMessage = "Title is required")]
            [MaxLength(255, ErrorMessage = "Title cannot exceed 255 characters")]
            public string Title { get; set; } = null!;

            [Required(ErrorMessage = "Content is required")]
            public string Content { get; set; } = null!;

            public DateTime? ExpiryDate { get; set; }
        }

        // Add these to your existing ViewModel

        public class EditAnnouncementViewModel
        {
            [Required]
            public int AnnouncementId { get; set; }

            [Required]
            public int CommunityId { get; set; }

            [Required(ErrorMessage = "Title is required")]
            [MaxLength(255, ErrorMessage = "Title cannot exceed 255 characters")]
            public string Title { get; set; } = null!;

            [Required(ErrorMessage = "Content is required")]
            public string Content { get; set; } = null!;

            public DateTime? ExpiryDate { get; set; }

            public bool IsActive { get; set; } = true;
        }

        public class DeleteAnnouncementViewModel
        {
            [Required]
            public int AnnouncementId { get; set; }

            [Required]
            public int CommunityId { get; set; }
        }

        public class AssignRoleRequest
        {
            public int CommunityId { get; set; }
            public int UserId { get; set; }
            public string NewRole { get; set; } = null!;
        }

        public class KickMemberRequest
        {
            public int CommunityId { get; set; }
            public int UserId { get; set; }
        }

        public class PrivacySettingsViewModel
        {
            [Required]
            public int CommunityId { get; set; }

            [Required(ErrorMessage = "Community type is required")]
            public string CommunityType { get; set; } = "Public";
        }

        public class ProfileImageViewModel
        {
            [Required]
            public int CommunityId { get; set; }

            public IFormFile? ProfileImage { get; set; }

            public string? CurrentImageUrl { get; set; }
        }

        public class DeleteCommunityViewModel
        {
            [Required]
            public int CommunityId { get; set; }

            [Required(ErrorMessage = "Confirmation is required")]
            [Display(Name = "Type community name to confirm")]
            [StringLength(150)]
            public string ConfirmationName { get; set; } = null!;

            [Display(Name = "Reason for deletion")]
            [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters")]
            [Required(ErrorMessage = "Please provide a reason for deletion")]
            public string DeleteReason { get; set; } = null!; // Now required

            public bool NotifyMembers { get; set; } = true;
        }
    }
}