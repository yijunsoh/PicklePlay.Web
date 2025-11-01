using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    [Table("User")]
    [Index(nameof(Email), IsUnique = true)]
    public class User
    {
        [Key]
        [Column("user_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("username")]
        public string Username { get; set; } = null!;

        [Required]
        [MaxLength(255)]
        [Column("email")]
        public string Email { get; set; } = null!;

        [MaxLength(512)]
        [Column("profile_picture")]
        public string? ProfilePicture { get; set; }

        [MaxLength(20)]
        [Column("phoneNo")]
        public string? PhoneNo { get; set; }

        // ADD GENDER FIELD
        [MaxLength(10)]
        [Column("gender")]
        public string? Gender { get; set; }

        [Required]
        [MaxLength(255)]
        [Column("password")]
        public string Password { get; set; } = null!;

        [Column("dateOfBirth", TypeName = "date")]
        public DateTime? DateOfBirth { get; set; }

        [Column("age")]
        public int? Age { get; set; }

        [Column("bio", TypeName = "text")]
        public string? Bio { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Active";

        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        [Column("emailVerify")]
        public bool EmailVerify { get; set; } = false;

        [Required]
        [MaxLength(30)]
        [Column("role")]
        public string Role { get; set; } = "Player";

        [Column("email_verification_token")]
        public string? EmailVerificationToken { get; set; }

        [Column("email_verified_at")]
        public DateTime? EmailVerifiedAt { get; set; }

        [Column("verification_token_expiry")]
        public DateTime? VerificationTokenExpiry { get; set; }

        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpiry { get; set; }

        public void GenerateEmailVerificationToken()
        {
            EmailVerificationToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("=", "")
                .Replace("+", "")
                .Replace("/", "");
            VerificationTokenExpiry = DateTime.UtcNow.AddMinutes(1); // Fixed to 1 minutes
        }
        public void GeneratePasswordResetToken()
        {
            PasswordResetToken = Guid.NewGuid().ToString();

            // Set expiry time to 1 minute
            PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(1);
        }
    }
}