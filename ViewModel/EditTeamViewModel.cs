using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PicklePlay.Models.ViewModels
{
    public class EditTeamViewModel
    {
        [Required]
        public int TeamId { get; set; }

        [Required(ErrorMessage = "Team name is required")]
        [MaxLength(100)]
        [Display(Name = "Team Name")]
        public string ?TeamName { get; set; }

        [Display(Name = "Team Icon")]
        public IFormFile? TeamIconFile { get; set; }
    }
}