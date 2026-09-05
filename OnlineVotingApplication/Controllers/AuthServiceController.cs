using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Text;

namespace OnlineVotingApplication.Controllers
{
    [EnableRateLimiting("StandardPolicy")] // Default policy for controller actions
    public class AuthServiceController : Controller
    {
        private readonly iAuthService _AuthService;
        private readonly ILogger<AuthServiceController> _ilogger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signManager;

        public AuthServiceController(
            iAuthService AuthService,
            ILogger<AuthServiceController> ilogger,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _AuthService = AuthService;
            _ilogger = ilogger;
            _userManager = userManager;
            _signManager = signInManager;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult UserRegistration()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")] // Protect registration against bot spam
        public async Task<IActionResult> UserRegistration(RegistrationViewModel model, string roles = "Voter")
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var newUserResponse = await _AuthService.RegisterUser(model, roles);
            if (!newUserResponse.Success)
            {
                foreach (var error in newUserResponse.Errors!)
                {
                    ModelState.AddModelError(string.Empty, error);
                }
                TempData["Error"] = newUserResponse.Message ?? "Registration failed.";
                return View(model);
            }

            // Automatically trigger the confirmation email send process post-registration
            return RedirectToAction(nameof(SendConfirmationToken), new { userId = newUserResponse.Data?.Id });
        }

        [HttpGet]
        public async Task<IActionResult> SendConfirmationToken(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found, please register.";
                return RedirectToAction(nameof(UserRegistration));
            }

            // 1. Generate the security token
            string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            // 2. Build the callback URL pointing back to this controller
            string confirmationLink = Url.Action("Confirm_Email", "AuthService",
                new { userId = user.Id, token = encodedToken }, Request.Scheme)!;

            // 3. Send the email via your service channel
            var result = await _AuthService.SendConfirmationTokenAsync(user, confirmationLink);

            if (!result.Success)
            {
                TempData["Error"] = "Failed to send confirmation email. Please try again.";
                return RedirectToAction(nameof(UserRegistration));
            }

            TempData["Success"] = "A confirmation link has been sent to your email!";
            return RedirectToAction(nameof(ConfirmEmailSent));
        }

        [HttpGet]
        public IActionResult ConfirmEmailSent() => View();

        [HttpGet]
        public async Task<IActionResult> Confirm_Email(string token, string userId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "Token expired or user not found.";
                return RedirectToAction(nameof(UserRegistration));
            }

            var userFind = await _userManager.FindByIdAsync(userId);
            if (userFind == null)
            {
                TempData["Error"] = "User doesn't exist. Please register.";
                return RedirectToAction(nameof(UserRegistration));
            }

            var isConfirmed = await _AuthService.ConfirmEmailAsync(userId, token);
            if (!isConfirmed)
            {
                TempData["Error"] = "Account could not be confirmed. The token may have expired. Please try again.";
                return RedirectToAction(nameof(UserRegistration));
            }

            TempData["Info"] = "User account confirmed successfully!";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        [EnableRateLimiting("StrictPolicy")] // Critical protection against brute-force password guessing
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please correct the errors below.";
                return View(model);
            }

            var (result, is2fa, message) = await _AuthService.LoginUserAsync(model);

            if (is2fa)
            {
                return RedirectToAction(nameof(LoginWith2fa), new { rememberMe = model.RememberMe });
            }

            if (result.Succeeded)
            {
                var user = await _userManager.FindByEmailAsync(model.EmailAddress ?? "");
                if (user != null)
                {
                    if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                        return RedirectToAction("Dashboard", "SuperAdminDashboard");

                    if (await _userManager.IsInRoleAsync(user, "Official"))
                        return RedirectToAction("Dashboard", "Tenant");
                    if (await _userManager.IsInRoleAsync(user, "Candidate"))
                        return RedirectToAction("Index", "Candidate");
                    if (await _userManager.IsInRoleAsync(user, "Auditor"))
                        return RedirectToAction("Index", "Auditor");
                }

                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
                return RedirectToAction("Lockout");

            ModelState.AddModelError(string.Empty, message ?? "Invalid login attempt.");
            TempData["ErrorMessage"] = message ?? "Invalid login attempt.";
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> LoginWith2fa(bool rememberMe)
        {
            var user = await _signManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));

            var sent = await _AuthService.TwoFactorAuthentication(user);
            if (!sent)
            {
                TempData["Error"] = "Could not send 2FA code. Try again.";
                return RedirectToAction(nameof(Login));
            }

            var model = new LoginWith2faViewModel
            {
                UserId = user.Id!,
                RememberMe = rememberMe
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")] // Prevent automated 2FA code guessing
        public async Task<IActionResult> LoginWith2fa(LoginWith2faViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            if (string.IsNullOrEmpty(model.UserId))
            {
                return RedirectToAction(nameof(Login));
            }

            var response = await _AuthService.ConfirmTwoFactorAsync(model.UserId, model.TwoFactorCode ?? "", model.RememberMe);

            if (response != null && response.Success)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, "Invalid authentication code.");
            return View(model);
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")] // Prevent denial-of-service / email-flooding attacks via password recovery
        public async Task<IActionResult> ForgotPassword(ForgotPasswordVM model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email ?? "");
            if (user == null) return RedirectToAction("ForgotPasswordConfirmation");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var callbackUrl = Url.Action("ResetPassword", "AuthService",
                new { token = encodedToken, email = user.Email }, Request.Scheme);

            await _AuthService.ForgotPasswordAsync(user, callbackUrl!);

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null) return RedirectToAction(nameof(Login));
            return View(new ResetPasswordViewModel { Token = token, Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")] // Prevent rapid-fire password reset attempts
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email ?? "");
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            var response = await _AuthService.ResetPasswordAsync(user, model.Token!, model.Password!);

            if (response.Success) return RedirectToAction(nameof(ResetPasswordConfirmation));

            ModelState.AddModelError(string.Empty, response.Message!);
            return View(model);
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation() => View();

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordDTO model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = _userManager.GetUserId(User);
            var response = await _AuthService.ChangePasswordAsync(userId!, model);

            if (response.Success)
            {
                TempData["Success"] = "Password updated successfully!";
                return RedirectToAction("Profile");
            }

            ModelState.AddModelError(string.Empty, response.Message!);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogoutUser()
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User is not logged in.";
                return RedirectToAction("Index", "Home");
            }

            try
            {
                await _signManager.SignOutAsync();
                TempData["SuccessMessage"] = "Logged out successfully.";

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Logout failed: " + ex.Message;
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LockUser(string userId)
        {
            var response = await _AuthService.LockOutUserAsync(userId);

            if (response.Success)
            {
                TempData["Status"] = "User has been banned.";
            }
            else
            {
                TempData["Error"] = response.Message;
            }

            return RedirectToAction("UserList", "Admin");
        }
    }
}