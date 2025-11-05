using System.ComponentModel.DataAnnotations;

namespace PicklePlay.Models.ViewModels
{
    public class CreateTeamViewModel
    {
        [Required]
        public int ScheduleId { get; set; }

        [Required(ErrorMessage = "Please enter a team name.")]
        [MaxLength(100)]
        [Display(Name = "Team Name")]
        public required string TeamName { get; set; }

        [Display(Name = "Team Icon (Optional)")]
        public IFormFile? TeamIconFile { get; set; }
    }
}