using System.ComponentModel.DataAnnotations;

namespace PicklePlay.ViewModels
{
    public class EditProfileModel
    {
        public int UserId { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address format")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [RegularExpression(@"^(\+?6?01)[0-46-9]-*[0-9]{7,8}$",
            ErrorMessage = "Please enter a valid Malaysian phone number (e.g., +6012-3456789 or 012-3456789)")]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Gender")]
        public string? Gender { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Date of Birth")]
        [AgeValidation(18, 120, ErrorMessage = "You must be at least 18 years old")]
        public DateTime? DateOfBirth { get; set; }

        public int? Age { get; set; }

        [StringLength(500, ErrorMessage = "Bio cannot exceed 500 characters")]
        [Display(Name = "Bio")]
        public string? Bio { get; set; }

        public IFormFile? ProfileImage { get; set; }
        public string? CurrentProfileImagePath { get; set; }

        // ✔ Return type is nullable; signature matches CustomValidation pattern
        public class AgeValidationAttribute : ValidationAttribute
{
    private readonly int _minAge;
    private readonly int _maxAge;

    public AgeValidationAttribute(int minAge, int maxAge)
    {
        _minAge = minAge;
        _maxAge = maxAge;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is DateTime dateOfBirth)
        {
            var age = DateTime.Today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > DateTime.Today.AddYears(-age)) age--;

            if (age < _minAge || age > _maxAge)
            {
                return new ValidationResult(ErrorMessage);
            }
        }
        
        return ValidationResult.Success;
    }
}
    }
}
