using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace OnlineVotingApplication.Controllers
{
    [AllowAnonymous]
    public class ExternalAuthController : Controller
    {
        private readonly iExternalAuthService _externalAuthService;
        private readonly IAppleAuthService _appleAuthService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<ExternalAuthController> _logger;

        #region Constructor
        public ExternalAuthController(
            iExternalAuthService externalAuthService,
            IAppleAuthService appleAuthService,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<ExternalAuthController> logger)
        {
            _externalAuthService = externalAuthService;
            _appleAuthService = appleAuthService;
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }
        #endregion

        #region Initial Challenge Actions
        [HttpPost]
        public IActionResult GoogleAuth(string? returnUrl = null)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "ExternalAuth", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
            return Challenge(properties, "Google");
        }

        [HttpPost]
        public IActionResult AppleAuth(string? returnUrl = null)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "ExternalAuth", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Apple", redirectUrl);
            return Challenge(properties, "Apple");
        }
        #endregion

        #region Standard Web Redirect Callback
        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
        {
            string fallbackLoginPath = Url.Content("~/");

            if (remoteError != null)
            {
                _logger.LogError("PROVIDER REMOTE ERROR: {RemoteError}", remoteError);
                TempData["Error"] = $"Error from external provider: {remoteError}";
                return Redirect(fallbackLoginPath);
            }

            // Diagnostic check to verify cookies are reaching Render
            foreach (var cookie in Request.Cookies)
            {
                _logger.LogInformation("Incoming Cookie -> Key: {Key}, Value Length: {Length}", cookie.Key, cookie.Value?.Length);
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                _logger.LogError("CRITICAL: GetExternalLoginInfoAsync returned null. External cookie was dropped on votezy.com.ng.");
                TempData["Error"] = "Error loading external login information.";
                return Redirect(fallbackLoginPath);
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var firstName = info.Principal.FindFirstValue(ClaimTypes.GivenName) ?? info.Principal.Identity?.Name ?? "External";
            var lastName = info.Principal.FindFirstValue(ClaimTypes.Surname) ?? "User";

            if (string.IsNullOrEmpty(email))
            {
                _logger.LogError("External provider did not return an email address.");
                TempData["Error"] = "External provider did not return an email address.";
                return Redirect(fallbackLoginPath);
            }

            // Look up if user already exists in local DB
            var user = await _userManager.FindByEmailAsync(email);
            bool isNewUserRegistration = false;

            if (user == null)
            {
                isNewUserRegistration = true;

                // 1. Verify token/provider key validity using your internal services
                ServiceResponse<ApplicationUser> authResponse;
                if (info.LoginProvider == "Google")
                {
                    authResponse = await _externalAuthService.AuthenticateGoogleUserAsync(info.ProviderKey);
                }
                else
                {
                    authResponse = await _externalAuthService.AuthenticateAppleUserAsync(info.ProviderKey, firstName, lastName);
                }

                if (!authResponse.Success)
                {
                    _logger.LogError("External validation failed for {Email}: {Message}", email, authResponse.Message);
                    TempData["Error"] = authResponse.Message ?? "External validation failed.";
                    return Redirect(fallbackLoginPath);
                }

                // 2. Instantiate and Save the User to Identity Database
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    FullName = $"{firstName} {lastName}",
                    TenantId = null,
                    PhoneNumber = info.Principal.FindFirstValue(ClaimTypes.MobilePhone) ?? info.Principal.FindFirstValue(ClaimTypes.HomePhone)
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    _logger.LogError("User creation failed for {Email}: {Errors}", email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                    TempData["Error"] = $"User creation failed: {string.Join(", ", createResult.Errors.Select(e => e.Description))}";
                    return Redirect(fallbackLoginPath);
                }

                // 3. Link external login provider identity info to the newly created local user
                var linkLoginResult = await _userManager.AddLoginAsync(user, info);
                if (!linkLoginResult.Succeeded)
                {
                    _logger.LogError("Failed to link external login for {Email}", email);
                    TempData["Error"] = "Failed to link external login identity provider to account.";
                    return Redirect(fallbackLoginPath);
                }

                // 4. Grant user the designated Voter security access role
                if (!await _userManager.IsInRoleAsync(user, "Voter"))
                {
                    await _userManager.AddToRoleAsync(user, "Voter");
                }
            }
            else
            {
                // Existing user: Link the provider login if they haven't logged in with this provider before
                var logins = await _userManager.GetLoginsAsync(user);
                if (!logins.Any(l => l.LoginProvider == info.LoginProvider && l.ProviderKey == info.ProviderKey))
                {
                    await _userManager.AddLoginAsync(user, info);
                }
            }

            // Sign the verified user into the HTTP Context Session
            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("{Email} logged in successfully via web flow ({Provider}).", email, info.LoginProvider);

            // 1. Respect explicit local returnUrl if available
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl != "/" && returnUrl != fallbackLoginPath)
            {
                return LocalRedirect(returnUrl);
            }

            // 2. Dynamic Role-based Routing
            var roles = await _userManager.GetRolesAsync(user);

            // Handle runtime tracking context sync for brand new voters
            if (isNewUserRegistration && !roles.Contains("Voter"))
            {
                roles.Add("Voter");
            }

            if (roles.Contains("SuperAdmin"))
                return RedirectToAction("Dashboard", "SuperAdminDashboard");
            if (roles.Contains("Official"))
                return RedirectToAction("Dashboard", "Tenant");
            if (roles.Contains("Candidate"))
                return RedirectToAction("Index", "Candidate");
            if (roles.Contains("Auditor"))
                return RedirectToAction("Index", "Auditor");
            if (roles.Contains("Voter"))
                return RedirectToAction("Index", "Voter");

            // 3. Ultimate fallback
            _logger.LogWarning("User {Email} authenticated successfully but had no matching role, falling back to Home Index.", email);
            return RedirectToAction("Index", "Home");
        }
        #endregion

        #region API / Direct Token Callbacks (JSON Responses)
        [HttpPost("google-callback")]
        public async Task<IActionResult> GoogleCallback([FromBody] GoogleTokenRequestModel model)
        {
            if (string.IsNullOrEmpty(model?.AccessToken))
            {
                return BadRequest(new { success = false, message = "Google Access Token is missing." });
            }

            var authResponse = await _externalAuthService.AuthenticateGoogleUserAsync(model.AccessToken);

            if (!authResponse.Success || authResponse.Data == null)
            {
                return BadRequest(new { success = false, message = authResponse.Message });
            }

            var roles = await _userManager.GetRolesAsync(authResponse.Data);
            if (!roles.Contains("Voter") && !roles.Any())
            {
                await _userManager.AddToRoleAsync(authResponse.Data, "Voter");
            }

            await _signInManager.SignInAsync(authResponse.Data, isPersistent: false);
            return Ok(new { success = true, message = "Logged in successfully", userId = authResponse.Data.Id });
        }

        [HttpPost("apple-callback")]
        public async Task<IActionResult> AppleCallback([FromForm] string code, [FromForm] string? user)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest(new { success = false, message = "Authorization code from Apple is missing." });
            }

            try
            {
                string firstName = "Apple";
                string lastName = "User";

                if (!string.IsNullOrEmpty(user))
                {
                    using (JsonDocument doc = JsonDocument.Parse(user))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("name", out JsonElement nameElement))
                        {
                            firstName = nameElement.TryGetProperty("firstName", out JsonElement fName) ? fName.GetString() ?? "Apple" : "Apple";
                            lastName = nameElement.TryGetProperty("lastName", out JsonElement lName) ? lName.GetString() ?? "User" : "User";
                        }
                    }
                }

                var authResponse = await _externalAuthService.AuthenticateAppleUserAsync(code, firstName, lastName);

                if (!authResponse.Success || authResponse.Data == null)
                {
                    return BadRequest(new { success = false, message = authResponse.Message ?? "Apple authentication failed." });
                }

                var roles = await _userManager.GetRolesAsync(authResponse.Data);
                if (!roles.Contains("Voter") && !roles.Any())
                {
                    await _userManager.AddToRoleAsync(authResponse.Data, "Voter");
                    roles.Add("Voter");
                }

                await _signInManager.SignInAsync(authResponse.Data, isPersistent: false);
                _logger.LogInformation("User logged in successfully via Apple endpoint API.");

                return Ok(new
                {
                    success = true,
                    message = "Logged in successfully",
                    userId = authResponse.Data.Id,
                    roles = roles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Apple Callback payload structural translation.");
                return BadRequest(new { success = false, message = "Authentication runtime execution failed." });
            }
        }
        #endregion

        #region Helpers
        private string GenerateAppleClientSecret()
        {
            return "YOUR_GENERATED_APPLE_CLIENT_SECRET_JWT";
        }
        #endregion
    }
}