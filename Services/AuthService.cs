using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using System.Text.Json;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public AuthService(ApplicationDbContext db, IEmailService email, ILogger<AuthService> logger, IConfiguration configuration, HttpClient httpClient)
    {
        _db = db;
        _email = email;
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, Func<int, string, string> buildVerificationLink)
    {
        // Normalize & validate
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim();

        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            return new RegisterResult { Success = false, Error = "Email is already registered." };
        }

        // Hash password
        var passwordHash = HashPassword(request.PasswordPlain);

        // Prepare new user (Pending Verification)
        var user = new User
        {
            Username = username,
            Email = email,
            Password = passwordHash,
            Status = "Pending Verification",
            EmailVerify = false,
            CreatedDate = DateTime.UtcNow,
            Role = "Player"
        };
        user.GenerateEmailVerificationToken();

        // Use a transaction: only keep the user if email sending succeeds
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var link = buildVerificationLink(user.UserId, user.EmailVerificationToken!);

            var html = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
            <h2 style='color: #A1D336; text-align: center;'>Welcome to PicklePlay!</h2>
            <p>Hello <strong>{System.Net.WebUtility.HtmlEncode(user.Username)}</strong>,</p>
            <p>Please verify your PicklePlay account by clicking the button below (valid for 5 minutes):</p>
            
            <div style='text-align: center; margin: 30px 0;'>
                <a href='{link}' style='
                    background-color: #A1D336; 
                    color: white; 
                    padding: 15px 30px; 
                    text-decoration: none; 
                    border-radius: 25px; 
                    font-weight: bold;
                    font-size: 16px;
                    display: inline-block;
                    border: none;
                    cursor: pointer;'>
                    Verify Email Address
                </a>
            </div>
            
            <p>Or copy and paste this link in your browser:</p>
            <p style='
                background-color: #f8f9fa; 
                padding: 15px; 
                border-radius: 5px; 
                word-break: break-all; 
                font-family: monospace;
                border-left: 4px solid #A1D336;'>
                {link}
            </p>
            
            <div style='
                background-color: #fff3cd; 
                border: 1px solid #ffeaa7; 
                border-radius: 5px; 
                padding: 15px; 
                margin: 20px 0;'>
                <strong>⚠️ Important:</strong> This link will expire in 5 minutes.
            </div>
            
            <p>If you did not request this account, please ignore this email.</p>
            
            <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;'>
            <p style='color: #666; font-size: 12px; text-align: center;'>
                This is an automated message from PicklePlay. Please do not reply to this email.
            </p>
        </div>";

            await _email.SendEmailAsync(user.Email, "Verify your PicklePlay account", html);

            await tx.CommitAsync();
            return new RegisterResult { Success = true, UserId = user.UserId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed; rolling back. Email={Email}", email);
            await tx.RollbackAsync();
            return new RegisterResult { Success = false, Error = "Failed to send verification email. Please try again later." };
        }
    }

    public async Task<bool> VerifyEmailAsync(int userId, string token)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return false;

        if (string.IsNullOrWhiteSpace(user.EmailVerificationToken) ||
            user.EmailVerificationToken != token ||
            user.VerificationTokenExpiry == null ||
            DateTime.UtcNow > user.VerificationTokenExpiry.Value)
        {
            return false;
        }

        user.EmailVerify = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.EmailVerificationToken = null;
        user.VerificationTokenExpiry = null;
        user.Status = "Active";

        await _db.SaveChangesAsync();
        return true;
    }
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant().Trim());
    }
    public async Task<bool> ResendVerificationAsync(string emailRaw, Func<int, string, string> buildVerificationLink)
{
    var email = emailRaw.Trim().ToLowerInvariant();
    var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user == null) return false;

    // Check if email is already verified - if yes, return false to indicate no email was sent
    if (user.EmailVerify) 
    {
        // Log this for debugging (optional)
        _logger.LogInformation("Resend verification attempted for already verified email: {Email}", email);
        return false; // Changed from true to false
    }

    user.GenerateEmailVerificationToken();
    await _db.SaveChangesAsync();

    var link = buildVerificationLink(user.UserId, user.EmailVerificationToken!);
    var html = $@"
    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
        <h2 style='color: #A1D336; text-align: center;'>New Verification Link</h2>
        <p>Hello <strong>{System.Net.WebUtility.HtmlEncode(user.Username)}</strong>,</p>
        <p>Here is your new verification link as requested (valid for 5 minutes):</p>
        
        <div style='text-align: center; margin: 30px 0;'>
            <a href='{link}' style='
                background-color: #A1D336; 
                color: white; 
                padding: 15px 30px; 
                text-decoration: none; 
                border-radius: 25px; 
                font-weight: bold;
                font-size: 16px;
                display: inline-block;
                border: none;
                cursor: pointer;'>
                Verify Email Address
            </a>
        </div>
        
        <p>Or copy and paste this link in your browser:</p>
        <p style='
            background-color: #f8f9fa; 
            padding: 15px; 
            border-radius: 5px; 
            word-break: break-all; 
            font-family: monospace;
            border-left: 4px solid #A1D336;'>
            {link}
        </p>
        
        <div style='
            background-color: #fff3cd; 
            border: 1px solid #ffeaa7; 
            border-radius: 5px; 
            padding: 15px; 
            margin: 20px 0;'>
            <strong>⚠️ Important:</strong> This link will expire in 5 minutes.
        </div>
        
        <p>If you did not request a new verification link, please ignore this email.</p>
        
        <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;'>
        <p style='color: #666; font-size: 12px; text-align: center;'>
            This is an automated message from PicklePlay. Please do not reply to this email.
        </p>
    </div>";

    await _email.SendEmailAsync(user.Email, "Your New PicklePlay Verification Link", html);
    return true;
}

    public async Task<bool> ValidateCaptchaAsync(string captchaToken)
    {
        // For testing, allow bypass if configured
        if (_configuration["BypassCaptcha"] == "true")
            return true;

        if (string.IsNullOrEmpty(captchaToken))
            return false;

        try
        {
            var secretKey = _configuration["Recaptcha:SecretKey"];
            var response = await _httpClient.GetStringAsync($"https://www.google.com/recaptcha/api/siteverify?secret={secretKey}&response={captchaToken}");

            using var jsonDoc = JsonDocument.Parse(response);
            var success = jsonDoc.RootElement.GetProperty("success").GetBoolean();
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAPTCHA validation failed");
            return false;
        }
    }

    private static string HashPassword(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[16];
        rng.GetBytes(salt);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);

        var payload = new byte[1 + salt.Length + hash.Length];
        payload[0] = 1;
        Buffer.BlockCopy(salt, 0, payload, 1, salt.Length);
        Buffer.BlockCopy(hash, 0, payload, 1 + salt.Length, hash.Length);

        return Convert.ToBase64String(payload);
    }
    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string email, string password)
{
    try
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null)
        {
            return new AuthenticationResult
            {
                Success = false,
                Error = "Invalid email or password."
                // User is null by default - which is correct for failed auth
            };
        }

        bool isPasswordValid = VerifyPassword(password, user.Password);

        if (!isPasswordValid)
        {
            return new AuthenticationResult
            {
                Success = false,
                Error = "Invalid email or password."
                // User is null - we don't return user info for failed attempts
            };
        }

        return new AuthenticationResult
        {
            Success = true,
            User = user // Only set User when authentication is successful
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Authentication error for email: {Email}", email);
        return new AuthenticationResult
        {
            Success = false,
            Error = "An error occurred during authentication."
            // User remains null for errors
        };
    }
}

// Add this password verification method to your AuthService class
private bool VerifyPassword(string plainPassword, string storedHash)
{
    try
    {
        // Decode the stored hash
        var payload = Convert.FromBase64String(storedHash);
        if (payload.Length < 1) return false;

        // Extract salt and hash from payload
        var version = payload[0];
        if (version != 1) return false; // Only support version 1 for now

        var salt = new byte[16];
        var storedHashBytes = new byte[32];
        
        Buffer.BlockCopy(payload, 1, salt, 0, salt.Length);
        Buffer.BlockCopy(payload, 1 + salt.Length, storedHashBytes, 0, storedHashBytes.Length);

        // Compute hash of the provided password
        using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, 100_000, HashAlgorithmName.SHA256);
        var computedHash = pbkdf2.GetBytes(32);

        // Compare hashes
        return computedHash.SequenceEqual(storedHashBytes);
    }
    catch
    {
        return false;
    }
}
}