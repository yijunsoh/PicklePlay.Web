using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Models
{
    // Ensures Email is unique at the DB level
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

        // Store a hash here (even though column name is "password" per data dictionary)
        [Required]
        [MaxLength(255)]
        [Column("password")]
        public string Password { get; set; } = null!;

        // DATE in MySQL (no time); nullable if you allow users to skip it
        [Column("dateOfBirth", TypeName = "date")]
        public DateTime? DateOfBirth { get; set; }

        // Kept because it’s in your dictionary (can also be computed in code if you prefer)
        [Column("age")]
        public int? Age { get; set; }

        [Column("bio", TypeName = "text")]
        public string? Bio { get; set; }

        // e.g., "Active", "Suspended", "Banned"
        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Active";

        // Default to UTC; you can also set default at DB level in OnModelCreating if preferred
        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        // 1/0 in MySQL maps to bool in EF Core
        [Column("emailVerify")]
        public bool EmailVerify { get; set; } = false;

        // e.g., "Player", "Admin"
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

        // Helper method to generate verification token
        public void GenerateEmailVerificationToken()
        {
            EmailVerificationToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("=", "")
                .Replace("+", "")
                .Replace("/", "");
            VerificationTokenExpiry = DateTime.UtcNow.AddMinutes(1); // Token valid for 24 hours
        }
    }
}
