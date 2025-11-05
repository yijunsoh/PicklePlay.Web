using System.ComponentModel.DataAnnotations;

namespace PicklePlay.ViewModels
{
    // ViewModel for the user submission form in Community.cshtml
    public class CommunityRequestSubmitViewModel
    {
        [Required]
        [StringLength(150, ErrorMessage = "Community Name cannot exceed 150 characters.")]
        public string CommunityName { get; set; } = null!;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string? Description { get; set; }

        [StringLength(150, ErrorMessage = "Location cannot exceed 150 characters.")]
        public string? CommunityLocation { get; set; }

        [Required]
        [StringLength(50)]
        public string CommunityType { get; set; } = "Public"; // Public or Private
    }

    // ViewModel for the Admin to view pending requests in CommunityRequests.cshtml
    public class CommunityRequestAdminViewModel
    {
        public int RequestId { get; set; }
        public string CommunityName { get; set; } = null!;
        public string RequesterUsername { get; set; } = null!;
        public DateTime RequestDate { get; set; }
        public string? Description { get; set; }
        public string? CommunityLocation { get; set; }
        public string CommunityType { get; set; } = null!;
    }
}