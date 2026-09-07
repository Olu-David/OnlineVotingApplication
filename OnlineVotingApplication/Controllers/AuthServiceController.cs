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
    [EnableRateLimiting("StandardPolicy")]
    public class AuthServiceController : Controller
    {
        private readonly iAuthService _authService;
        private readonly ILogger<AuthServiceController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        #region AuthServiceController
        public AuthServiceController(
            iAuthService authService,
            ILogger<AuthServiceController> logger,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _authService = authService;
            _logger = logger;
            _userManager = userManager;
            _signInManager = signInManager;
        }
        #endregion

        public IActionResult Index() => View();

        [HttpGet]
        [AllowAnonymous]
        public IActionResult UserRegistration() => View();

        #region UserRegistration
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> UserRegistration(RegistrationViewModel model, string roles = "Voter")
        {
            if (!ModelState.IsValid) return View(model);

            var newUserResponse = await _authService.RegisterUser(model, roles);
            if (!newUserResponse.Success)
            {
                foreach (var error in newUserResponse.Errors ?? Enumerable.Empty<string>())
                {
                    ModelState.AddModelError(string.Empty, error);
                }
                TempData["Error"] = newUserResponse.Message ?? "Registration failed.";
                return View(model);
            }

            return RedirectToAction(nameof(SendConfirmationToken), new { userId = newUserResponse.Data?.Id });
        }
        #endregion

        #region SendConfirmationToken
        [HttpGet]
        public async Task<IActionResult> SendConfirmationToken(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found, please register.";
                return RedirectToAction(nameof(UserRegistration));
            }

            string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            string confirmationLink = Url.Action("Confirm_Email", "AuthService",
                new { userId = user.Id, token = encodedToken }, Request.Scheme)!;

            var result = await _authService.SendConfirmationTokenAsync(user, confirmationLink);
            if (!result.Success)
            {
                TempData["Error"] = "Failed to send confirmation email. Please try again.";
                return RedirectToAction(nameof(UserRegistration));
            }

            TempData["Success"] = "A confirmation link has been sent to your email!";
            return RedirectToAction(nameof(ConfirmEmailSent));
        }
        #endregion

        [HttpGet]
        public IActionResult ConfirmEmailSent() => View();

        #region Confirm_Email
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Confirm_Email(string token, string userId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "Invalid payload or token expired.";
                return RedirectToAction(nameof(UserRegistration));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User doesn't exist. Please register.";
                return RedirectToAction(nameof(UserRegistration));
            }

            // Decode Base64Url token back to string
            string decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));

            var isConfirmed = await _authService.ConfirmEmailAsync(userId, decodedToken);
            if (!isConfirmed)
            {
                TempData["Error"] = "Account could not be confirmed. The token may have expired.";
                return RedirectToAction(nameof(UserRegistration));
            }

            TempData["Info"] = "User account confirmed successfully!";
            return RedirectToAction(nameof(Login));
        }
        #endregion

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login() => View();

        #region Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please correct the errors below.";
                return View(model);
            }

            var (result, is2fa, message) = await _authService.LoginUserAsync(model);

            if (is2fa)
            {
                return RedirectToAction(nameof(LoginWith2fa), new { rememberMe = model.RememberMe });
            }

            if (result.Succeeded)
            {
                var user = await _userManager.FindByEmailAsync(model.EmailAddress ?? "");
                if (user != null)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("SuperAdmin")) return RedirectToAction("Dashboard", "SuperAdminDashboard");
                    if (roles.Contains("Official")) return RedirectToAction("Dashboard", "Tenant");
                    if (roles.Contains("Candidate")) return RedirectToAction("Index", "Candidate");
                    if (roles.Contains("Auditor")) return RedirectToAction("Index", "Auditor");
                    if (roles.Contains("Voter")) return RedirectToAction("Index", "Voter");
                }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            if (result.IsLockedOut) return RedirectToAction("Lockout");

            ModelState.AddModelError(string.Empty, message ?? "Invalid login attempt.");
            TempData["ErrorMessage"] = message ?? "Invalid login attempt.";
            return View(model);
        }
        #endregion

        #region LoginWith2fa
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWith2fa(bool rememberMe)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));

            var sent = await _authService.TwoFactorAuthentication(user);
            if (!sent)
            {
                TempData["Error"] = "Could not send 2FA code. Try again.";
                return RedirectToAction(nameof(Login));
            }

            return View(new LoginWith2faViewModel { UserId = user.Id, RememberMe = rememberMe });
        }
        #endregion

        #region LoginWith2fa (2)
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> LoginWith2fa(LoginWith2faViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            if (string.IsNullOrEmpty(model.UserId)) return RedirectToAction(nameof(Login));

            var response = await _authService.ConfirmTwoFactorAsync(model.UserId, model.TwoFactorCode ?? "", model.RememberMe);
            if (response != null && response.Success)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, "Invalid authentication code.");
            return View(model);
        }
        #endregion

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword() => View();

        #region ForgotPassword
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordVM model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email ?? "");
            if (user == null) return RedirectToAction("ForgotPasswordConfirmation");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var callbackUrl = Url.Action("ResetPassword", "AuthService",
                new { token = encodedToken, email = user.Email }, Request.Scheme);

            await _authService.ForgotPasswordAsync(user, callbackUrl!);

            return RedirectToAction("ForgotPasswordConfirmation");
        }
        #endregion

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation() => View();

        #region ResetPassword
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null) return RedirectToAction(nameof(Login));
            return View(new ResetPasswordViewModel { Token = token, Email = email });
        }
        #endregion

        #region ResetPassword (2)
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("StrictPolicy")]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email ?? "");
            if (user == null) return RedirectToAction(nameof(ResetPasswordConfirmation));

            string decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token!));
            var response = await _authService.ResetPasswordAsync(user, decodedToken, model.Password!);

            if (response.Success) return RedirectToAction(nameof(ResetPasswordConfirmation));

            ModelState.AddModelError(string.Empty, response.Message!);
            return View(model);
        }
        #endregion

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation() => View();

        #region ChangePassword
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordDTO model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = _userManager.GetUserId(User);
            var response = await _authService.ChangePasswordAsync(userId!, model);

            if (response.Success)
            {
                TempData["Success"] = "Password updated successfully!";
                return RedirectToAction("Profile");
            }

            ModelState.AddModelError(string.Empty, response.Message!);
            return View(model);
        }
        #endregion

        #region LogoutUser
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogoutUser()
        {
            try
            {
                await _signInManager.SignOutAsync();
                TempData["SuccessMessage"] = "Logged out successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during logout.");
                TempData["ErrorMessage"] = "Logout failed.";
            }

            return RedirectToAction("Index", "Home");
        }
        #endregion

        #region LockUser
        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LockUser(string userId)
        {
            var response = await _authService.LockOutUserAsync(userId);
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
        #endregion
    }
}