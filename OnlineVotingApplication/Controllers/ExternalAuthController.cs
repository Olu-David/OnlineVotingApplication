using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System.Security.Claims;

namespace OnlineVotingApplication.Controllers
{
    [AllowAnonymous]
    public class ExternalAuthController : Controller
    {
        private readonly iExternalAuthService _externalAuthService;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<ExternalAuthController> _logger;

        public ExternalAuthController(
            iExternalAuthService externalAuthService,
            SignInManager<ApplicationUser> signInManager,
            ILogger<ExternalAuthController> logger)
        {
            _externalAuthService = externalAuthService;
            _signInManager = signInManager;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult GoogleAuth(string? returnUrl = null, string assignedRole = "Voter")
        {
            // Save the role temporarily in cookies or query string so we know it after redirect
            string redirectUrl = Url.Action(nameof(ExternalLoginCallback), "ExternalAuth", new { returnUrl, assignedRole }) ?? string.Empty;
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
            return Challenge(properties, "Google");
        }

        [HttpGet]
        public IActionResult AppleAuth(string? returnUrl = null, string assignedRole = "Voter")
        {
            string redirectUrl = Url.Action(nameof(ExternalLoginCallback), "ExternalAuth", new { returnUrl, assignedRole }) ?? string.Empty;
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Apple", redirectUrl);
            return Challenge(properties, "Apple");
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? assignedRole = "Voter", string? remoteError = null)
        {
            returnUrl ??= Url.Content("~/");

            if (remoteError != null)
            {
                TempData["Error"] = $"Error from external provider: {remoteError}";
                return RedirectToAction("Login", "AuthService");
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                TempData["Error"] = "Error loading external login information.";
                return RedirectToAction("Login", "AuthService");
            }

            // Extract user details from the external provider's claims
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var nameIdentifier = info.ProviderKey;
            var firstName = info.Principal.FindFirstValue(ClaimTypes.GivenName) ?? info.Principal.Identity?.Name ?? string.Empty;
            var lastName = info.Principal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;

            if (string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "External provider did not return an email address.";
                return RedirectToAction("Login", "AuthService");
            }

            // Route through your custom service pipeline to handle tenant checks, roles, and DB saving
            ServiceResponse<ApplicationUser> authResponse;

            if (info.LoginProvider == "Google")
            {
                // If you are using standard middleware, you can pass the email/key directly or adapt your service.
                // Alternatively, leverage your service's pipeline logic:
                authResponse = await _externalAuthService.AuthenticateGoogleUserAsync(nameIdentifier, assignedRole ?? "Voter");
            }
            else
            {
                authResponse = await _externalAuthService.AuthenticateAppleUserAsync(nameIdentifier, firstName, lastName, assignedRole ?? "Voter");
            }

            if (!authResponse.Success || authResponse.Data == null)
            {
                TempData["Error"] = authResponse.Message;
                return RedirectToAction("Login", "AuthService");
            }

            // Sign the user in locally using ASP.NET Core Identity
            await _signInManager.SignInAsync(authResponse.Data, isPersistent: false);
            _logger.LogInformation("{Email} logged in successfully via {Provider}.", email, info.LoginProvider);

            return LocalRedirect(returnUrl);
        }
    }
}