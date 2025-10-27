using Microsoft.AspNetCore.Mvc;
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

        public AuthController(IAuthService authService, IConfiguration configuration, HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
        {
            _authService = authService;
            _configuration = configuration;
            _httpClient = httpClient;
            _httpContextAccessor = httpContextAccessor;
        }

        // GET: /Auth/Login
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: /Auth/Login
        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Login(string email, string password)
{
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
    {
        ModelState.AddModelError("", "Please enter both email and password.");
        return View();
    }

    try
    {
        var result = await _authService.AuthenticateAsync(email, password);

        if (result.Success && result.User != null) // DOUBLE CHECK - both Success and User
        {
            var user = result.User;
            
            if (!user.EmailVerify)
            {
                ModelState.AddModelError("", "Please verify your email before logging in.");
                return View();
            }

            if (user.Status != "Active")
            {
                ModelState.AddModelError("", "Your account is not active. Please contact support.");
                return View();
            }

            // Store user info in session
            _httpContextAccessor.HttpContext?.Session?.SetString("UserEmail", user.Email);
            _httpContextAccessor.HttpContext?.Session?.SetString("UserName", user.Username);
            _httpContextAccessor.HttpContext?.Session?.SetInt32("UserId", user.UserId);

            return RedirectToAction("Community", "Home");
        }
        else
        {
            ModelState.AddModelError("", result.Error ?? "Invalid email or password.");
            return View();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Login error: {ex.Message}");
        ModelState.AddModelError("", "An error occurred during login. Please try again.");
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
                // Store email for display on success page
                TempData["UserEmail"] = model.Email;
                TempData["SuccessMessage"] = "Registration successful! Please check your email to verify your account.";

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
                    TempData["SuccessMessage"] = "Your email is already verified. Please login.";
                    return RedirectToAction("Login", "Auth");
                }
            }

            // If still no email, redirect to login page
            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "Please complete the signup process first.";
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
                TempData["ErrorMessage"] = "Invalid verification link. Please use the link from your email.";
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
                TempData["ErrorMessage"] = "Invalid request. Please use the resend button on the verification page.";

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
                TempData["SuccessMessage"] = "New verification link has been sent to your email!";
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
                    TempData["ErrorMessage"] = "This email is already verified. No need to resend verification.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to send verification email. Please try again.";
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
    }
}