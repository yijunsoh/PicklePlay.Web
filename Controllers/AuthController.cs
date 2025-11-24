using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.ViewModels;
using System.Text.Json;

namespace PicklePlay.Controllers
{
    public class AuthController : Controller
    {
        private readonly IAuthService _authService;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _context;

        public AuthController(IAuthService authService, IConfiguration configuration, HttpClient httpClient, IHttpContextAccessor httpContextAccessor, ApplicationDbContext context)
        {
            _authService = authService;
            _configuration = configuration;
            _httpClient = httpClient;
            _httpContextAccessor = httpContextAccessor;
            _context = context;


        }

        // GET: /Auth/Login
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            // 1. Basic Input Validation
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ViewData["LoginError"] = "Please enter both email and password.";
                return View();
            }

            try
            {
                // 2. Authenticate
                var result = await _authService.AuthenticateAsync(email, password);

                if (result.Success && result.User != null)
                {
                    var user = result.User;

                    // 3. Status Checks
                    if (!user.EmailVerify)
                    {
                        ViewData["LoginError"] = "Please verify your email before logging in.";
                        return View();
                    }

                    if (user.Status != "Active" && user.Status != "Suspended")
                    {
                        ViewData["LoginError"] = "Your account is not active. Please contact support.";
                        return View();
                    }

                    // 4. Session Setup
                    _httpContextAccessor.HttpContext?.Session?.SetString("UserEmail", user.Email);
                    _httpContextAccessor.HttpContext?.Session?.SetString("UserName", user.Username);
                    _httpContextAccessor.HttpContext?.Session?.SetInt32("UserId", user.UserId);
                    _httpContextAccessor.HttpContext?.Session?.SetString("UserRole", user.Role);
                    _httpContextAccessor.HttpContext?.Session?.SetString("ProfileImagePath", user.ProfilePicture ?? "");

                    // 5. Redirect based on Role
                    if (user.Role == "Admin")
                    {
                        return RedirectToAction("AdminDashboard", "Admin");
                    }
                    else
                    {
                        return RedirectToAction("Community", "Home");
                    }
                }
                else
                {
                    // Auth failed (Wrong password/User not found)
                    ViewData["LoginError"] = result.Error ?? "Invalid email or password.";
                    return View();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login error: {ex.Message}");
                ViewData["LoginError"] = "An error occurred during login. Please try again.";
                return View();
            }
        }

        // Add this logout method as well
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            // Clear session
            _httpContextAccessor.HttpContext?.Session?.Clear();
            return RedirectToAction("Login", "Auth");
        }

        // GET: /Auth/Signup
        [HttpGet]
        public IActionResult Signup()
        {
            var model = new SignupViewModel();
            ViewBag.RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];
            return View(model);
        }

        // POST: /Auth/Signup
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Signup(SignupViewModel model)
        {
            // Always set the reCAPTCHA site key
            ViewBag.RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Validate CAPTCHA
            if (!await ValidateCaptchaAsync(model.CaptchaToken))
            {
                ModelState.AddModelError("", "CAPTCHA validation failed. Please try again.");
                return View(model);
            }

            // Convert SignupViewModel to RegisterRequest
            var registerRequest = new RegisterRequest
            {
                Username = model.FullName,
                Email = model.Email,
                PasswordPlain = model.Password
            };

            // Build verification link
            string BuildVerificationLink(int userId, string token)
            {
                return Url.Action("VerifyEmail", "Auth", new { userId = userId, token = token }, Request.Scheme)!;
            }

            var result = await _authService.RegisterAsync(registerRequest, BuildVerificationLink);

            if (result.Success)
            {
                // Get the newly created user to create their wallet
                var newUser = await _authService.GetUserByEmailAsync(model.Email);
                if (newUser != null)
                {
                    var newWallet = new Wallet
                    {
                        UserId = newUser.UserId,
                        WalletBalance = 0,
                        EscrowBalance = 0,
                        TotalSpent = 0,
                        LastUpdated = DateTime.UtcNow
                    };

                    _context.Wallets.Add(newWallet);
                    await _context.SaveChangesAsync();
                }
                // Store email for display on success page
                TempData["UserEmail"] = model.Email;
                TempData["AuthSuccess"] = "Registration successful! Please check your email to verify your account.";

                // Redirect to success page
                return RedirectToAction("SignupSuccess");
            }
            else
            {
                ModelState.AddModelError("", result.Error!);
                ViewBag.RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];
                return View(model);
            }
        }

        // GET: /Auth/SignupSuccess
        [HttpGet]
        public IActionResult SignupSuccess()
        {
            // Check both TempData and Session
            var userEmail = TempData.Peek("UserEmail") as string;
            if (string.IsNullOrEmpty(userEmail))
            {
                userEmail = _httpContextAccessor.HttpContext?.Session?.GetString("LastSignupEmail");
            }

            // Additional check: Verify the user hasn't already verified their email
            if (!string.IsNullOrEmpty(userEmail))
            {
                var user = _authService.GetUserByEmailAsync(userEmail).Result;
                if (user != null && user.EmailVerify)
                {
                    // User already verified, clear session and redirect to login
                    _httpContextAccessor.HttpContext?.Session?.Remove("LastSignupEmail");
                    TempData["AuthSuccess"] = "Your email is already verified. Please login.";
                    return RedirectToAction("Login", "Auth");
                }
            }

            // If still no email, redirect to login page
            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["AuthError"] = "Please complete the signup process first.";
                return RedirectToAction("Login", "Auth");
            }

            var resendSuccess = TempData["ResendSuccess"] as bool?;

            ViewBag.ResendSuccess = resendSuccess ?? false;
            ViewBag.UserEmail = userEmail;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> VerifyEmail(int userId, string token)
        {
            // === SECURITY CHECK: Prevent direct URL access without proper parameters ===
            if (userId <= 0 || string.IsNullOrEmpty(token))
            {
                TempData["AuthError"] = "Invalid verification link. Please use the link from your email.";
                return RedirectToAction("Login", "Auth");
            }

            TempData["VerifyUserId"] = userId;
            TempData["VerifyToken"] = token;
            TempData.Keep("VerifyUserId");
            TempData.Keep("VerifyToken");

            var user = await _authService.GetUserByIdAsync(userId);
            if (user != null && user.EmailVerify)
            {
                ViewBag.Message = "Email already verified!";
                ViewBag.Success = true;
                ViewBag.ShowButton = false;
                ViewBag.ShowResendButton = false;
                ViewBag.UserEmail = user.Email;
                return View();
            }
            if (user == null || user.EmailVerificationToken != token)
            {
                ViewBag.Message = "Invalid verification link.";
                ViewBag.Success = false;
                ViewBag.ShowButton = false;
                ViewBag.ShowResendButton = true;
                ViewBag.UserEmail = user?.Email;
                ViewBag.IsEmailVerified = user?.EmailVerify ?? false;
            }
            else if (user.VerificationTokenExpiry < DateTime.UtcNow)
            {
                ViewBag.Message = "Verification link has expired.";
                ViewBag.Success = false;
                ViewBag.ShowButton = false;
                ViewBag.ShowResendButton = true;
                ViewBag.UserEmail = user.Email;
                ViewBag.UserId = user.UserId;
                ViewBag.IsEmailVerified = user.EmailVerify;
            }
            else if (user.EmailVerify)
            {
                ViewBag.Message = "Email already verified!";
                ViewBag.Success = true;
                ViewBag.ShowButton = false;
                ViewBag.ShowResendButton = false;
                ViewBag.IsEmailVerified = true;
            }
            else
            {
                ViewBag.Message = "Click the button below to verify your email address.";
                ViewBag.Success = false;
                ViewBag.ShowButton = true;
                ViewBag.ShowResendButton = false;
                ViewBag.UserEmail = user.Email;
                ViewBag.UserId = user.UserId;
                ViewBag.IsEmailVerified = false;
            }

            return View();
        }

        // POST: /Auth/VerifyEmail - Handle button click
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmailPost()
        {
            // Use Peek to read without removing from TempData
            var userId = TempData.Peek("VerifyUserId") as int?;
            var token = TempData.Peek("VerifyToken") as string;

            if (!userId.HasValue || string.IsNullOrEmpty(token))
            {
                ViewBag.Message = "Invalid verification request.";
                ViewBag.Success = false;
                ViewBag.ShowButton = false;
                ViewBag.ShowResendButton = true;
                return View("VerifyEmail");
            }

            var success = await _authService.VerifyEmailAsync(userId.Value, token);

            if (success)
            {
                // Clear the session data after successful verification
                var user = await _authService.GetUserByIdAsync(userId.Value);
                if (user != null)
                {
                    _httpContextAccessor.HttpContext?.Session?.Remove("LastSignupEmail");
                }

                ViewBag.Message = "Email verified successfully! You can now login.";
                ViewBag.Success = true;
                ViewBag.ShowResendButton = false;
            }
            else
            {
                ViewBag.Message = "Invalid or expired verification link.";
                ViewBag.Success = false;
                ViewBag.ShowResendButton = true;
                ViewBag.UserId = userId.Value;

                // Get user email for resend functionality
                var user = await _authService.GetUserByIdAsync(userId.Value);
                ViewBag.UserEmail = user?.Email;
            }

            ViewBag.ShowButton = false;
            return View("VerifyEmail");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerification(string email, string fromPage = "SignupSuccess")
        {
            // === SECURITY CHECK: Prevent direct URL access ===
            if (string.IsNullOrEmpty(email))
            {
                TempData["AuthError"] = "Invalid request. Please use the resend button on the verification page.";

                // Redirect based on where the invalid request might have come from
                if (fromPage == "VerifyEmail")
                    return RedirectToAction("VerifyEmail", new { userId = 0, token = "" });
                else
                    return RedirectToAction("SignupSuccess");
            }

            // Build verification link function
            string BuildVerificationLink(int userId, string token)
            {
                return Url.Action("VerifyEmail", "Auth", new { userId = userId, token = token }, Request.Scheme)!;
            }

            var success = await _authService.ResendVerificationAsync(email, BuildVerificationLink);

            if (success)
            {
                TempData["ResendSuccess"] = true;
                TempData["AuthSuccess"] = "New verification link has been sent to your email!";
                TempData["UserEmail"] = email;

                if (fromPage == "VerifyEmail")
                {
                    var user = await _authService.GetUserByEmailAsync(email);
                    if (user != null)
                    {
                        return RedirectToAction("VerifyEmail", new { userId = user.UserId, token = user.EmailVerificationToken });
                    }
                    return RedirectToAction("VerifyEmail", new { userId = 0, token = "" });
                }
                else
                {
                    return RedirectToAction("SignupSuccess");
                }
            }
            else
            {
                // Check if the failure is because email is already verified
                var user = await _authService.GetUserByEmailAsync(email);
                if (user != null && user.EmailVerify)
                {
                    TempData["AuthError"] = "This email is already verified. No need to resend verification.";
                }
                else
                {
                    TempData["AuthError"] = "Failed to send verification email. Please try again.";
                }

                if (fromPage == "VerifyEmail")
                    return RedirectToAction("VerifyEmail", new { userId = 0, token = "" });
                else
                    return RedirectToAction("SignupSuccess");
            }
        }
        // GET: /Auth/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }
        // POST: /Auth/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ModelState.AddModelError("", "Please enter your email address.");
                return View();
            }

            // Build reset link function
            string BuildResetLink(int userId, string token)
            {
                return Url.Action("ResetPassword", "Auth", new { userId = userId, token = token }, Request.Scheme)!;
            }

            var success = await _authService.GeneratePasswordResetTokenAsync(email, BuildResetLink);

            if (success)
            {
                TempData["AuthSuccess"] = "Password reset email has been sent successfully! Please check your inbox.";
                return RedirectToAction("ForgotPassword");
            }
            else
            {
                TempData["AuthError"] = "Email not found. Please check your email address.";
                return View();
            }
        }

        // GET: /Auth/ResetPassword
        [HttpGet]
        public async Task<IActionResult> ResetPassword(int userId, string token)
        {
            if (userId <= 0 || string.IsNullOrEmpty(token))
            {
                TempData["AuthError"] = "Invalid password reset link.";
                return RedirectToAction("Login");
            }

            var user = await _authService.ValidatePasswordResetTokenAsync(userId, token);
            if (user == null)
            {
                TempData["AuthError"] = "Invalid or expired password reset link.";
                return RedirectToAction("Login");
            }

            // Store in TempData for the POST action
            TempData["ResetUserId"] = userId;
            TempData["ResetToken"] = token;
            TempData.Keep("ResetUserId");
            TempData.Keep("ResetToken");

            var model = new ResetPasswordViewModel
            {
                UserId = userId,
                Token = token
            };

            return View(model);
        }

        // POST: /Auth/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Get from TempData (in case of model binding issues)
            var userId = TempData.Peek("ResetUserId") as int?;
            var token = TempData.Peek("ResetToken") as string;

            if (!userId.HasValue || string.IsNullOrEmpty(token))
            {
                TempData["AuthError"] = "Invalid password reset request.";
                return RedirectToAction("Login");
            }

            var user = await _authService.ValidatePasswordResetTokenAsync(userId.Value, token);
            if (user == null)
            {
                TempData["AuthError"] = "Invalid or expired password reset link.";
                return RedirectToAction("Login");
            }

            var success = await _authService.ResetPasswordAsync(userId.Value, model.NewPassword);
            if (success)
            {
                TempData["AuthSuccess"] = "Your password has been reset successfully! You can now login with your new password.";

                // FIX: Redirect to Login action instead of ResetPassword
                return RedirectToAction("Login");
            }

            return View(model);
        }

        // CAPTCHA validation method
        private async Task<bool> ValidateCaptchaAsync(string captchaToken)
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

                // Proper JSON parsing
                using var jsonDoc = JsonDocument.Parse(response);
                var success = jsonDoc.RootElement.GetProperty("success").GetBoolean();
                return success;
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"CAPTCHA validation error: {ex.Message}");
                return false;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerificationFromLogin(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                TempData["LoginError"] = "Please enter your email address.";
                TempData["PrefilledEmail"] = email;
                return RedirectToAction("Login");
            }

            try
            {
                // 1. Check if user exists
                var user = await _authService.GetUserByEmailAsync(email);
                if (user == null)
                {
                    // Security: Don't reveal user existence, but show success message
                    TempData["LoginSuccess"] = "If your email is registered, a verification link has been sent to your inbox.";
                    TempData["PrefilledEmail"] = email;
                    return RedirectToAction("Login");
                }

                // 2. Check if already verified
                if (user.EmailVerify)
                {
                    TempData["LoginSuccess"] = "Your email is already verified. You can login now.";
                    TempData["PrefilledEmail"] = email;
                    return RedirectToAction("Login");
                }

                // 3. Define Link Builder
                string BuildVerificationLink(int userId, string token)
                {
                    return Url.Action("VerifyEmail", "Auth", new { userId = userId, token = token }, Request.Scheme)!;
                }

                // 4. Send Email
                var success = await _authService.ResendVerificationAsync(email, BuildVerificationLink);

                if (success)
                {
                    TempData["LoginSuccess"] = "New verification link has been sent to your email! Please check your inbox.";
                    TempData["PrefilledEmail"] = email;
                }
                else
                {
                    TempData["LoginError"] = "Failed to send verification email. Please try again.";
                    TempData["PrefilledEmail"] = email;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ResendVerificationFromLogin error: {ex.Message}");
                TempData["LoginError"] = "An error occurred while sending the email. Please try again.";
                TempData["PrefilledEmail"] = email;
            }

            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string email, string currentPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            {
                TempData["AuthError"] = "Please fill in all password fields.";
                return RedirectToAction("EditProfile", "Home");
            }

            try
            {
                // Verify current password
                var authResult = await _authService.AuthenticateAsync(email, currentPassword);
                if (!authResult.Success)
                {
                    TempData["AuthError"] = "Current password is incorrect.";
                    return RedirectToAction("EditProfile", "Home");
                }

                // Change password
                var user = await _authService.GetUserByEmailAsync(email);
                if (user == null)
                {
                    TempData["AuthError"] = "User not found.";
                    return RedirectToAction("EditProfile", "Home");
                }

                var success = await _authService.ResetPasswordAsync(user.UserId, newPassword);
                if (success)
                {
                    TempData["AuthSuccess"] = "Password changed successfully!";
                }
                else
                {
                    TempData["AuthError"] = "Failed to change password. Please try again.";
                }
            }
            catch (Exception)
            {
                TempData["AuthError"] = "An error occurred while changing your password.";

            }

            return RedirectToAction("EditProfile", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPaymentPassword(string password)
        {
            try
            {
                var currentUserId = HttpContext.Session.GetInt32("UserId");
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Use the new dedicated method
                var authResult = await _authService.VerifyPaymentPasswordAsync(currentUserId.Value, password);

                if (authResult.Success)
                {
                    return Json(new { success = true });
                }
                else
                {
                    return Json(new { success = false, message = authResult.Error });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error verifying payment password: {ex.Message}");
                return Json(new { success = false, message = "Error verifying password" });
            }
        }
    }
}