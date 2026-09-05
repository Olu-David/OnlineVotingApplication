using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Repository.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

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

        // ─── INITIAL CHALLENGE ACTIONS (Fixes the 404 Error) ─────────────

        [HttpPost]
        [AllowAnonymous]
        public IActionResult GoogleAuth(string? returnUrl = null)
        {
            // Request a redirect to Google's authentication page
            var redirectUrl = Url.Action("ExternalLoginCallback", "ExternalAuth", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
            return Challenge(properties, "Google");
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult AppleAuth(string? returnUrl = null)
        {
            // Request a redirect to Apple's authentication page
            var redirectUrl = Url.Action("ExternalLoginCallback", "ExternalAuth", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Apple", redirectUrl);
            return Challenge(properties, "Apple");
        }

        // ─── CALLBACKS ───────────────────────────────────────────────────

        [HttpPost("google-callback")]
        public async Task<IActionResult> GoogleCallback(string accessToken)
        {
            var authResponse = await _externalAuthService.AuthenticateGoogleUserAsync(accessToken, "Voter");

            if (!authResponse.Success || authResponse.Data == null)
            {
                return BadRequest(new { success = false, message = authResponse.Message });
            }

            await _signInManager.SignInAsync(authResponse.Data, isPersistent: false);
            return Ok(new { success = true, Message = "Logged in successfully", UserId = authResponse.Data.Id });
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? assignedRole = "Voter", string? remoteError = null)
        {
            returnUrl ??= Url.Content("~/");

            if (remoteError != null)
            {
                TempData["Error"] = $"Error from external provider: {remoteError}";
                return RedirectToAction("Login", "Account"); // Pointing to standard account login route
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                TempData["Error"] = "Error loading external login information.";
                return RedirectToAction("Login", "Account");
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var nameIdentifier = info.ProviderKey;
            var firstName = info.Principal.FindFirstValue(ClaimTypes.GivenName) ?? info.Principal.Identity?.Name ?? string.Empty;
            var lastName = info.Principal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;

            if (string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "External provider did not return an email address.";
                return RedirectToAction("Login", "Account");
            }

            ServiceResponse<ApplicationUser> authResponse;

            if (info.LoginProvider == "Google")
            {
                authResponse = await _externalAuthService.AuthenticateGoogleUserAsync(nameIdentifier, assignedRole ?? "Voter");
            }
            else
            {
                authResponse = await _externalAuthService.AuthenticateAppleUserAsync(nameIdentifier, firstName, lastName, assignedRole ?? "Voter");
            }

            if (!authResponse.Success || authResponse.Data == null)
            {
                TempData["Error"] = authResponse.Message;
                return RedirectToAction("Login", "Account");
            }

            await _signInManager.SignInAsync(authResponse.Data, isPersistent: false);
            _logger.LogInformation("{Email} logged in successfully via {Provider}.", email, info.LoginProvider);

            return LocalRedirect(returnUrl);
        }

        [HttpPost("apple-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> AppleCallback([FromForm] string code, [FromForm] string? user)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest(new { success = false, message = "Authorization code from Apple is missing." });
            }

            try
            {
                string clientSecret = GenerateAppleClientSecret();
                var tokenResponse = await _appleAuthService.ValidateAuthorizationCodeAsync(code, clientSecret);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.IdToken))
                {
                    return BadRequest(new { success = false, message = "Failed to validate authorization code with Apple." });
                }

                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(tokenResponse.IdToken);

                var email = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                            ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

                var appleSubId = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                               ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                if (string.IsNullOrEmpty(email))
                {
                    return BadRequest(new { success = false, message = "Could not extract email from Apple token." });
                }

                string firstName = "Apple";
                string lastName = "User";
                if (!string.IsNullOrEmpty(user))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(user);
                        if (doc.RootElement.TryGetProperty("name", out var nameProp))
                        {
                            firstName = nameProp.TryGetProperty("firstName", out var f) ? f.GetString() ?? "Apple" : "Apple";
                            lastName = nameProp.TryGetProperty("lastName", out var l) ? l.GetString() ?? "User" : "User";
                        }
                    }
                    catch { /* Fallback name tracking */ }
                }

                var authResponse = await _externalAuthService.AuthenticateAppleUserAsync(appleSubId ?? email, firstName, lastName, "Voter");

                if (!authResponse.Success || authResponse.Data == null)
                {
                    return BadRequest(new { success = false, message = authResponse.Message });
                }

                await _signInManager.SignInAsync(authResponse.Data, isPersistent: false);
                return Ok(new { success = true, message = "Authenticated successfully via Apple", userId = authResponse.Data.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Apple sign-in callback.");
                return StatusCode(500, new { success = false, message = "An error occurred while processing Apple authentication." });
            }
        }

        private string GenerateAppleClientSecret()
        {
            return "YOUR_GENERATED_APPLE_CLIENT_SECRET_JWT";
        }
    }
}