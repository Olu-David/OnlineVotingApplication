using AspNetCoreGeneratedDocument;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Text;

namespace OnlineVotingApplication.Controllers
{
   
    public class AuthServiceController : Controller
    {
        private readonly iAuthService _AuthService;
        private readonly ILogger<AuthServiceController> _ilogger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signManager;

        public AuthServiceController(iAuthService AuthService, ILogger<AuthServiceController> ilogger, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
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
        public async Task<IActionResult> UserRegistration(RegistrationViewModel model,string Roles)
        {
           if(!ModelState.IsValid)
            {
                return View();  
            }
           var NewUser= await _AuthService.RegisterUser(model);
            if(!NewUser.Success)
            {
                return RedirectToAction(nameof(HomeController), "Index");
            }
            return View(model);

        }
        [HttpGet]

        public async Task<IActionResult> SendConfirmationToken(ApplicationUser user)
        {
            if (user == null)
            {
                TempData["Error"] = "User not found, please register.";
                return RedirectToAction(nameof(UserRegistration));
            }

            // 1. Generate the security token
            string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            string EncodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            // 2. Build the callback URL (Points to the action that validates the token)
            string confirmationLink = Url.Action("Confirm_Email", "User",
                new { userId = user.Id, EncodedToken }, Request.Scheme)!;

            // 3. Send the email via your integrated SendGrid/Mail service
            var result = await _AuthService.SendConfirmationTokenAsync(user, confirmationLink);

            if (!result.Success)
            {
                TempData["Error"] = "Failed to send email. Please try again.";
                return RedirectToAction(nameof(UserRegistration));
            }

            // 4. IMPORTANT: Redirect to a "Success Notification" page, NOT the confirmation action itself
            TempData["Success"] = "A confirmation link has been sent to your email!";
            return RedirectToAction(nameof(ConfirmEmailSent)); // Create a simple view that says "Check your inbox"
        }
        [HttpGet]
        public IActionResult ConfirmEmailSent() => View();

        [HttpGet]
        public async Task<IActionResult> Confirm_Email(string token, string userId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "Token Expired or User not Found";
                return RedirectToAction(nameof(SendConfirmationToken));
            }
            var userFind = await _userManager.FindByIdAsync(userId);
            if (userFind == null)
            {
                TempData["Error"] = "User doesnt Exist, Register";
                return RedirectToAction(nameof(UserRegistration));
            }
            string Encodedtoken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            var result = await _AuthService.ConfirmEmailAsync(userId, token);

            if (!result)
            {
                TempData["Error"] = "User's Account not confirmed Sucessfully, Token expired Try Again";
                return RedirectToAction(nameof(UserRegistration));
            }
            TempData["Info"] = "User Account Confirmed Successfully";
            return RedirectToAction(nameof(Login));
        }
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            // === STRONG DEBUGGING - Check this in Output Window ===
            if (!ModelState.IsValid)
            {
                Console.WriteLine("=== MODELSTATE INVALID ===");
                foreach (var state in ModelState)
                {
                    if (state.Value?.Errors.Count > 0)
                    {
                        var errors = string.Join(" | ", state.Value.Errors.Select(e => e.ErrorMessage));
                        Console.WriteLine($"FIELD: '{state.Key}' → Errors: {errors}");
                    }
                }
                Console.WriteLine("===========================");

                TempData["ErrorMessage"] = "Please correct the errors below.";
                return View(model);
            }

            // Rest of your logic (unchanged)
            var (result, is2fa, message) = await _AuthService.LoginUserAsync(model);

            if (is2fa)
            {
                return RedirectToAction("LoginWith2fa", new { ReturnUrl = returnUrl, model.RememberMe });
            }

            if (result.Succeeded)
            {
                var user = await _userManager.FindByEmailAsync(model.EmailAddress ?? "");
                if (user != null)
                {
                    if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                        return RedirectToAction("CreateCandidate", nameof(CandidateController));

                    if (await _userManager.IsInRoleAsync(user, "Admin"))
                        return RedirectToAction("Index", "AdminDashboard");

                    if (await _userManager.IsInRoleAsync(user, "Merchant"))
                        return RedirectToAction("Dashboard", "Merchant");

                    if (await _userManager.IsInRoleAsync(user, "Vendor"))
                        return RedirectToAction("Dashboard", "Vendor");
                }

                return RedirectToAction("Index", "Home");
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
            // Identity tracks who is "partially" logged in
            var user = await _signManager.GetTwoFactorAuthenticationUserAsync();

            if (user == null) return RedirectToAction(nameof(Login));

            // Step 1: Send the code using your service
            var sent = await _AuthService.TwoFactorAuthentication(user);

            if (!sent)
            {
                TempData["Error"] = "Could not send code. Try again.";
                return RedirectToAction(nameof(Login));
            }

            // Step 2: Show the view
            var model = new LoginWith2faViewModel
            {
                UserId = user.Id!,
                RememberMe = rememberMe
            };

            return View(model);
        }

        // POST: /User/LoginWith2fa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWith2fa(LoginWith2faViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // Step 3: Confirm using your service
            if (string.IsNullOrEmpty(model.UserId))
            {
                return RedirectToAction("Login");
            }
            var response = await _AuthService.ConfirmTwoFactorAsync(model.UserId, model.TwoFactorCode ?? "", model.RememberMe);

            if(response!=null)
            if (response.Success)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            // If it fails, show the error message from the service

            ModelState.AddModelError(string.Empty, "Invalid authencation code");
            return View(model);
        }

        // ---. FORGOT PASSWORD (GET) ---    
        [HttpGet]
        public IActionResult ForgotPassword() => View();


        
        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordVM model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email ?? "");
            if (user == null) return RedirectToAction("ForgotPasswordConfirmation");

            // 1. Generate Token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            // 2. Generate the full Link
            var callbackUrl = Url.Action("ResetPassword", "Account",
                new { token = encodedToken, email = user.Email }, Request.Scheme);

            // 3. Send it!
             _AuthService.ForgotPasswordAsync(user, callbackUrl!);

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        // ---. RESET PASSWORD (GET) ---
        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null) return RedirectToAction(nameof(Login));
            return View(new ResetPasswordViewModel { Token = token, Email = email });
        }

        // --- . RESET PASSWORD (POST) ---
        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email ?? "");
            var response = await _AuthService.ResetPasswordAsync(user!, model.Token!, model.Password!);

            if (response.Success) return RedirectToAction("Login");
           
            return View(model);
        }

        // --- 3. CHANGE PASSWORD (Inside Settings/Profile) ---
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordDTO model)
        {
            if (!ModelState.IsValid) return View(model);

            // Get Current Logged in User ID
            var userId = _userManager.GetUserId(User);
            var response = await _AuthService.ChangePasswordAsync(userId!, model);

            if (response.Success)
            {
                TempData["Success"] = "Password updated successfully!";
                return RedirectToAction("Profile");
            }

            //ModelState.AddModelError("", response.Message);
            return View(model);
        }

        // --- 4. LOCKOUT (Admin Only) ---
        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
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
             
