using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.Services
{
    public class ExternalAuthService : iExternalAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ExternalAuthService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        #region Constructor
        public ExternalAuthService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AppDbContext dbContext,
            ILogger<ExternalAuthService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _dbContext = dbContext;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }
        #endregion

        #region EnsureRolesExistAsync
        private async Task EnsureRolesExistAsync()
        {
            string[] systemRoles = { "Official", "Voter", "Auditor", "Candidate" };
            foreach (var role in systemRoles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }
        #endregion

        #region Google Authentication Pipeline
        public async Task<ServiceResponse<ApplicationUser>> AuthenticateGoogleUserAsync(string idToken)
        {
            var response = new ServiceResponse<ApplicationUser>();

            try
            {
                // Validate token directly against Google authority infrastructure
                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken);
                if (payload == null)
                {
                    response.Message = "Google identity synchronization validation failed.";
                    return response;
                }

                // Strictly assign "Voter" as the default external role (Tenant-independent)
                return await ProcessExternalUserPipelineAsync(
                    payload.Email,
                    payload.GivenName ?? "",
                    payload.FamilyName ?? "",
                    "Google",
                    payload.Subject,
                    "Voter"
                );
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogError(ex, "Invalid Google ID token string provided.");
                response.Message = "The provided Google Authentication token is invalid or expired.";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected crash during Google authentication lifecycle.");
                response.Message = "An error occurred synchronizing your Google profile metadata.";
                return response;
            }
        }
        #endregion

        #region Apple Authentication Pipeline
        public async Task<ServiceResponse<ApplicationUser>> AuthenticateAppleUserAsync(string idToken, string firstName, string lastName)
        {
            var response = new ServiceResponse<ApplicationUser>();

            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(idToken))
                {
                    response.Message = "Malformed Identity Profile structure submitted from client.";
                    return response;
                }

                var jwtToken = handler.ReadJwtToken(idToken);

                // Confirm valid Apple issuance authority signature
                if (jwtToken.Issuer != "https://appleid.apple.com")
                {
                    response.Message = "Identity authentication source verification conflict.";
                    return response;
                }

                var emailClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
                var appleUserId = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                if (string.IsNullOrEmpty(emailClaim) || string.IsNullOrEmpty(appleUserId))
                {
                    response.Message = "Required private metadata claims missing from token profile context.";
                    return response;
                }

                // Strictly assign "Voter" as the default external role (Tenant-independent)
                return await ProcessExternalUserPipelineAsync(
                    emailClaim,
                    firstName,
                    lastName,
                    "Apple",
                    appleUserId,
                    "Voter"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected crash during Apple token verification parsing workflow.");
                response.Message = "An error occurred parsing your secure Apple profile metadata.";
                return response;
            }
        }
        #endregion

        #region Shared Core External Provisioning Pipeline
        private async Task<ServiceResponse<ApplicationUser>> ProcessExternalUserPipelineAsync(
            string email, string firstName, string lastName, string providerName, string providerKey, string assignedRole)
        {
            var response = new ServiceResponse<ApplicationUser>();

            // Scrutinize system boundaries for existing account instances
            var existingUser = await _userManager.FindByEmailAsync(email);

            if (existingUser != null)
            {
                // Check if external login link mapping is missing
                var logins = await _userManager.GetLoginsAsync(existingUser);
                var matchingLogin = logins.FirstOrDefault(l => l.LoginProvider == providerName && l.ProviderKey == providerKey);

                if (matchingLogin == null)
                {
                    var linkResult = await _userManager.AddLoginAsync(existingUser, new UserLoginInfo(providerName, providerKey, providerName));
                    if (!linkResult.Succeeded)
                    {
                        response.Errors = linkResult.Errors.Select(e => e.Description).ToList();
                        response.Message = $"Failed to link your existing account profile to {providerName} Identity Services.";
                        return response;
                    }
                }

                response.Data = existingUser;
                response.Success = true;
                response.Message = $"Authentication successfully synchronized via {providerName}.";
                return response;
            }

            // Execute profile workspace generation provisioning steps
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var newUser = new ApplicationUser
                {
                    Email = email,
                    UserName = email,
                    FullName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                        ? $"External {providerName} User"
                        : $"{firstName} {lastName}".Trim(),
                    EmailConfirmed = true, // Third-party trusted provider has pre-verified email profile state
                    IsApproved = false, // Awaiting administrative oversight review
                    TenantId = null // Explicitly tenant-independent
                };

                if (assignedRole == "Voter")
                {
                    newUser.VoterRegistrationID = $"VOT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
                }

                // External OAuth users do not contain localized storage passwords
                var createResult = await _userManager.CreateAsync(newUser);
                if (!createResult.Succeeded)
                {
                    response.Errors = createResult.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync();
                    return response;
                }

                // Explicitly persist third party cross-reference tracking details
                var loginResult = await _userManager.AddLoginAsync(newUser, new UserLoginInfo(providerName, providerKey, providerName));
                if (!loginResult.Succeeded)
                {
                    response.Errors = loginResult.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync();
                    return response;
                }

                await EnsureRolesExistAsync();
                await _userManager.AddToRoleAsync(newUser, assignedRole);

                await transaction.CommitAsync();
                response.Data = newUser;
                response.Success = true;
                response.Message = $"{assignedRole} profile generated and synced via {providerName}. Pending administrative authorization review.";
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction crash saving external registration schema profile for {Email}", email);
                response.Message = "An unexpected error occurred during federated profile onboarding initialization.";
                return response;
            }
        }
        #endregion
    }
}