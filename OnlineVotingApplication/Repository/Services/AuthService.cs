using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Jobs;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.Services
{
    public class AuthService : iAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly AppDbContext _dbContext;
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<AuthService> _logger;
        private readonly NotificationChannel _channel;

        #region Constructor & Private Helpers
        public AuthService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SignInManager<ApplicationUser> signInManager,
            AppDbContext dbContext,
            ITenantProvider tenantProvider,
            ILogger<AuthService> logger,
            NotificationChannel channel)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _signInManager = signInManager;
            _dbContext = dbContext;
            _tenantProvider = tenantProvider;
            _logger = logger;
            _channel = channel;
        }

        private async Task EnsureRolesExistAsync(string[] roles)
        {
            foreach (var role in roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(role));
                    _logger.LogInformation("Role {Role} created", role);
                }
            }
        }
        #endregion

        #region Registration & Authentication
        public async Task<ServiceResponse<ApplicationUser>> RegisterUser(RegistrationViewModel model, string assignedRole = "Voter")
        {
            var response = new ServiceResponse<ApplicationUser>();
            var activeTenantId = _tenantProvider.GetCurrentTenantId();

            if (activeTenantId == Guid.Empty)
            {
                response.Message = "Invalid context: An official organization invite link is required.";
                return response;
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    var newUser = new ApplicationUser
                    {
                        Email = model.EmailAddress,
                        UserName = model.EmailAddress,
                        FullName = $"{model.FirstName} {model.LastName}",
                        PhoneNumber = model.PhoneNumber,
                        TenantId = activeTenantId,
                        EmailConfirmed = false,
                        IsApproved = false
                    };

                    if (assignedRole.Equals("Voter", StringComparison.OrdinalIgnoreCase))
                    {
                        newUser.VoterRegistrationID = $"VOT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
                    }

                    var result = await _userManager.CreateAsync(newUser, model.Password ?? "");
                    if (!result.Succeeded)
                    {
                        response.Errors = result.Errors.Select(e => e.Description).ToList();
                        await transaction.RollbackAsync();
                        return response;
                    }

                    await EnsureRolesExistAsync(new[] { assignedRole });
                    await _userManager.AddToRoleAsync(newUser, assignedRole);

                    await transaction.CommitAsync();
                    response.Data = newUser;
                    response.Success = true;
                    response.Message = "Registration successful. Awaiting approval.";
                    return response;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Registration failure for {Email}", model.EmailAddress);
                    response.Message = "An unexpected error occurred.";
                    return response;
                }
            });
        }

        public async Task<(SignInResult Result, bool RequiresTwoFactor, string? ErrorMessage)> LoginUserAsync(LoginViewModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.EmailAddress) || string.IsNullOrWhiteSpace(model.Password))
            {
                return (SignInResult.Failed, false, "Email and password are required.");
            }

            string normalizedEmail = model.EmailAddress.ToUpperInvariant();
            var user = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

            if (user == null)
            {
                return (SignInResult.Failed, false, "Invalid email or password combination.");
            }

            bool isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");

            if (!isSuperAdmin)
            {
                var currentTenantId = _tenantProvider.GetCurrentTenantId();

                if (currentTenantId == Guid.Empty && user.TenantId.HasValue && user.TenantId.Value != Guid.Empty)
                {
                    currentTenantId = user.TenantId.Value;
                    _tenantProvider.SetTenantContext(currentTenantId);
                }

                if (currentTenantId == Guid.Empty)
                {
                    return (SignInResult.Failed, false, "You must enter through an active organization domain.");
                }

                if (!user.TenantId.HasValue || user.TenantId.Value != currentTenantId)
                {
                    return (SignInResult.Failed, false, "Invalid email or password combination.");
                }
            }

            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                return (SignInResult.NotAllowed, false, "Please confirm your email before logging in.");
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded) return (result, false, null);
            if (result.RequiresTwoFactor) return (result, true, null);
            if (result.IsLockedOut) return (result, false, "Your account has been locked due to multiple failed login attempts.");
            if (result.IsNotAllowed) return (result, false, "Login is currently not allowed for this account.");

            return (SignInResult.Failed, false, "Invalid email or password combination.");
        }
        #endregion

        #region Email Confirmation & Token Lifecycle
        public async Task<ServiceResponse<ApplicationUser>> SendConfirmationTokenAsync(ApplicationUser user, string confirmationLink)
        {
            var response = new ServiceResponse<ApplicationUser>();
            try
            {
                if (user == null)
                {
                    response.Message = "User not found";
                    response.Success = false;
                    return response;
                }

                string subject = "Confirm your Account";
                string message = $@"
                    <h2>Welcome to VoteZy!</h2>
                    <p>Please click the button below to verify your email address:</p>
                    <a href='{confirmationLink}' style='background-color: #4CAF50; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Verify Email</a>
                    <p>If the button doesn't work, copy and paste this link: <br/> {confirmationLink}</p>";

                var emailJob = new NotificationJob(user.Email ?? "", subject, message, NotificationType.Email);
                await _channel.Writer.WriteAsync(emailJob);

                response.Data = user;
                response.Success = true;
                response.Message = "Verification email queued successfully.";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Email could not be queued.";
                response.Errors = new List<string> { ex.Message };
            }

            return response;
        }

        public async Task<bool> ConfirmEmailAsync(string userId, string token)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return false;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded) return false;

            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
            return true;
        }
        #endregion

        #region Password Management
        public async Task<bool> ForgotPasswordAsync(ApplicationUser user, string callbackUrl)
        {
            if (user == null || string.IsNullOrEmpty(callbackUrl)) return false;

            try
            {
                string subject = "Reset Your Password";
                string message = $@"
                    <div style='font-family: sans-serif; padding: 20px; border: 1px solid #eee;'>
                        <h2>Password Reset Request</h2>
                        <p>We received a request to reset your password. Click the button below to proceed:</p>
                        <a href='{callbackUrl}' style='display:inline-block; background-color: #ef4444; color: white; padding: 12px 25px; text-decoration: none; border-radius: 5px;'>Reset Password</a>
                        <br/><br/>
                        <hr style='border:none; border-top: 1px solid #eee;' />
                        <p style='font-size: 12px; color: #666;'>
                            If the button doesn't work, copy and paste this link into your browser:<br/>
                            {callbackUrl}
                        </p>
                        <p style='font-size: 12px; color: #666;'>If you did not request this, you can safely ignore this email.</p>
                    </div>";

                var emailJob = new NotificationJob(user.Email ?? "", subject, message, NotificationType.Email);
                await _channel.Writer.WriteAsync(emailJob);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue password reset email.");
                return false;
            }
        }

        public async Task<ServiceResponse<ApplicationUser>> ResetPasswordAsync(ApplicationUser user, string token, string password)
        {
            var response = new ServiceResponse<ApplicationUser>();

            if (user == null || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(password))
            {
                response.Success = false;
                response.Message = "Invalid request data.";
                return response;
            }

            try
            {
                string decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
                var result = await _userManager.ResetPasswordAsync(user, decodedToken, password);

                if (!result.Succeeded)
                {
                    response.Success = false;
                    response.Message = result.Errors.FirstOrDefault()?.Description ?? "Reset failed.";
                    return response;
                }

                response.Data = user;
                response.Success = true;
                response.Message = "Password Reset Successful";
            }
            catch (FormatException)
            {
                response.Success = false;
                response.Message = "The reset token is malformed.";
            }

            return response;
        }

        public async Task<ServiceResponse<ApplicationUser>> ChangePasswordAsync(string userId, ChangePasswordDTO model)
        {
            var response = new ServiceResponse<ApplicationUser>();
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null || string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(model.NewPassword))
            {
                response.Success = false;
                response.Message = "User details not found.";
                return response;
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                response.Success = false;
                response.Message = result.Errors.FirstOrDefault()?.Description ?? "Password change unsuccessful.";
                return response;
            }

            response.Data = user;
            response.Success = true;
            response.Message = "Password changed successfully.";
            return response;
        }
        #endregion

        #region Two-Factor Authentication (2FA)
        public async Task<bool> TwoFactorAuthentication(ApplicationUser user)
        {
            if (user == null || !user.TwoFactorEnabled) return false;

            var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);

            try
            {
                string subject = "Your 2FA Login Code";
                string message = $"Your security code is: <b>{token}</b>. It expires in 5 minutes.";

                var emailJob = new NotificationJob(user.Email ?? "", subject, message, NotificationType.Email);
                await _channel.Writer.WriteAsync(emailJob);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<ServiceResponse<ApplicationUser>> ConfirmTwoFactorAsync(string userId, string token, bool rememberMe)
        {
            var response = new ServiceResponse<ApplicationUser>();
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User session expired. Please login again.";
                return response;
            }

            bool isValid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, token);
            if (!isValid)
            {
                response.Success = false;
                response.Message = "Invalid or expired code.";
                return response;
            }

            await _signInManager.SignInAsync(user, rememberMe);
            response.Success = true;
            response.Data = user;
            return response;
        }

        public async Task<bool> SetTwoFactorAuthentication(ApplicationUser user)
        {
            if (user == null) return false;
            if (user.TwoFactorEnabled) return true;

            var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
            return result.Succeeded;
        }
        #endregion

        #region Account Administration & Lockout
        public async Task<ServiceResponse<ApplicationUser>> LockOutUserAsync(string userId)
        {
            var response = new ServiceResponse<ApplicationUser>();
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found.";
                return response;
            }

            var result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            if (!result.Succeeded)
            {
                response.Success = false;
                response.Message = "Failed to lock out user.";
                response.Errors = result.Errors.Select(e => e.Description).ToList();
                return response;
            }

            response.Data = user;
            response.Success = true;
            response.Message = "User has been successfully locked out.";
            return response;
        }
        #endregion
    }
}