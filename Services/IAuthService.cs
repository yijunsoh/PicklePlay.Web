using PicklePlay.Models;

public class RegisterResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? UserId { get; set; }
}

public class RegisterRequest
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordPlain { get; set; } = "";
}

// Add this class for authentication result
public class AuthenticationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public User? User { get; set; }
}

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, Func<int, string, string> buildVerificationLink);
    Task<bool> VerifyEmailAsync(int userId, string token);
    Task<bool> ResendVerificationAsync(string email, Func<int, string, string> buildVerificationLink);

    // Add the authentication method
    Task<AuthenticationResult> AuthenticateAsync(string email, string password);

    Task<bool> ValidateCaptchaAsync(string captchaToken);
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserByEmailAsync(string email);

    // Password reset methods
    Task<bool> GeneratePasswordResetTokenAsync(string email, Func<int, string, string> buildResetLink);
    Task<User?> ValidatePasswordResetTokenAsync(int userId, string token);
    Task<bool> ResetPasswordAsync(int userId, string newPassword);
    Task<AuthenticationResult> VerifyPaymentPasswordAsync(int userId, string password);

    Task<bool> UpdateUserProfileAsync(int userId, string fullName, string email, string? phoneNumber,
        string? gender, DateTime? dateOfBirth, string? bio, string? profileImagePath, string? location);
}