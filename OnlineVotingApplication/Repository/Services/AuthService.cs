using HashidsNet;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.Channels;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Enums;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using OnlineVotingApplication.Jobs;
using System.Data;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;

namespace OnlineVotingApplication.Repository.Services
{
    public class AuthService : iAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<AuthService> _logger;
        private readonly AppDbContext _DbContext;
        private const string SystemSalt = "YourCompanySuperSecretSalt123!";
        private readonly NotificationChannel _notificationChannel;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AuthService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger<AuthService> logger, AppDbContext dbContext, NotificationChannel notificationChannel, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
            _DbContext = dbContext;
            _notificationChannel = notificationChannel;
            _signInManager = signInManager;
        }
        private async Task EnsureRoleExists()
        {
            // 1. Define all the roles your app needs in one list
            string[] roles = { "Official", "Voter", "Auditor", "Candidate" };


            foreach (var role in roles)
            {
                // 2. Check if the role exists in the database
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    // 3. If it doesn't exist, create it
                    await _roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }

        public async Task<ServiceResponse<ApplicationUser>> RegistrationAsyncAlpha(RegistrationViewModel model, string Roles)
        {
            var response = new ServiceResponse<ApplicationUser>();
            _logger.LogInformation("Step 1: Starting registration for {Email}", model.EmailAddress);

            // Using 'using' ensures the transaction is disposed correctly
            using var transaction = await _DbContext.Database.BeginTransactionAsync();

            try
            {
                var newUser = new ApplicationUser
                {
                    Email = model.EmailAddress,
                    UserName = model.EmailAddress,
                    FullName = $"{model.FirstName} {model.LastName}",
                    PhoneNumber = model.PhoneNumber,
                    EmailConfirmed = false,

                };

                // 1. Create User
                var result = await _userManager.CreateAsync(newUser, model.Password);

                if (!result.Succeeded)
                {
                    _logger.LogWarning("Failed to create user {Email}", newUser.Email);
                    response.Errors = result.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync(); // Cancel everything
                    return response;
                }

                // 2. Ensure Role Exists
                await EnsureRoleExists();

                // 3. Add to Role
                var roleResult = await _userManager.AddToRoleAsync(newUser, Roles);
                if (!roleResult.Succeeded)
                {
                    _logger.LogError("Failed to add role to {Email}", newUser.Email);
                    response.Errors = roleResult.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync();
                    return response;
                }



                // --- THE CRITICAL STEP ---
                await transaction.CommitAsync();
                _logger.LogInformation("User {Email} registered and committed to DB", newUser.Email);

                response.Data = newUser;
                response.Success = true;
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Registration failed for {Email}", model.EmailAddress);
                response.Message = "An unexpected error occurred during registration.";
                return response;
            }
        }
        public async Task<ServiceResponse<ApplicationUser>> RegisterUser(RegistrationViewModel model)
        {
            var response = new ServiceResponse<ApplicationUser>();
            var transaction = await _DbContext.Database.BeginTransactionAsync();
            var VoterId = GenerateVoterRegistrationID();

            try
            {
                var user = new ApplicationUser
                {
                    FullName = $"{model.FirstName} {model.LastName}",
                    VoterRegistrationID = VoterId,
                    Email = model.EmailAddress,
                    UserName = model.EmailAddress
                };

                _logger.LogInformation("New User Creation Loading");
                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                {
                    _logger.LogError("Failed to create user {Email}", model.EmailAddress);
                    response.Errors = result.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync();
                    return response;
                }

                if (!await _roleManager.RoleExistsAsync("Voter"))
                {
                    _logger.LogInformation("Voter role does not exist. Creating it now.");
                    await _roleManager.CreateAsync(new IdentityRole("Voter"));
                }

                var roleResult = await _userManager.AddToRoleAsync(user, "Voter");
                if (!roleResult.Succeeded)
                {
                    _logger.LogError("Failed to add role to {Email}", user.Email);
                    response.Errors = roleResult.Errors.Select(e => e.Description).ToList();
                    await transaction.RollbackAsync();
                    return response;
                }

                await transaction.CommitAsync();

                _logger.LogInformation("User {Email} registered and committed to DB", user.Email);
                response.Data = user;
                response.Success = true;
                response.Message = "User Creation Successful";
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Registration failed for {Email}", model.EmailAddress);
                response.Message = "An unexpected error occurred during registration.";
                return response;
            }
        }



        private static string GenerateVoterRegistrationID()
        {
            var Initials = "VOT";
            var IdCreatedAt = DateTime.Now.ToString("yyyyMMdd");
            //int value = 0;
            var UniqueId = Guid.NewGuid().ToString("N").Substring(0, 6);

            return $"{Initials}-{IdCreatedAt}-{UniqueId}";
        }
        private static string GenerateSecureId(string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);

            // Allocates a fixed buffer space just for 32 bytes of hash data
            Span<byte> hashBytes = stackalloc byte[32];
            SHA256.HashData(inputBytes, hashBytes);

            return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(hashBytes).Substring(0, 12);
        }

        public async Task<ServiceResponse<ApplicationUser>> SendConfirmationTokenAsync(ApplicationUser user, string confirmationLink)
        {
            var response = new ServiceResponse<ApplicationUser>();
            try
            {
                // FindUser validation
                if (user == null)
                {
                    response.Message = "User not Found";
                    response.Success = false;
                    return response;
                }

                // 1. Prepare the content
                string subject = "Confirm your Account";
                string message = $@"
              <h2>Welcome to the platform!</h2>
              <p>Please click the button below to verify your email address:</p>
            <a href='{confirmationLink}' style='background-color: #4CAF50; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Verify Email</a>
            <p>If the button doesn't work, copy and paste this link: <br/> {confirmationLink}</p>";

              
                

                // 2. Create the notification job payload
                var emailJob = new NotificationJob(user.Email ?? "", subject, message, NotificationType.Email);
                

                // 3. Drop into the channel queue instantly (Non-blocking)
                // Note: Make sure '_notificationChannel' is injected into this class constructor
                await _notificationChannel.Writer.WriteAsync(emailJob);

                response.Data = user;
                // Updated message to reflect that it is queued for processing
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

        public async Task<bool> ConfirmEmailAsync(string UserId, string token)
        {
            if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(token))
            {
                return false;
            }
            var user = await _userManager.FindByIdAsync(UserId);
            if (user == null)
            {
                return false;
            }
            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                return false;
            }
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
            return true;
        }

        private async Task EnsureRolesExistAsync(string Roles)
        {
            if (!await _roleManager.RoleExistsAsync(Roles))
            {
                await _roleManager.CreateAsync(new IdentityRole(Roles));
                _logger.LogInformation("Role {Role} created", Roles);
            }
        }
        public async Task<(SignInResult Result, bool RequiresTwoFactor, string? ErrorMessage)> LoginUserAsync(LoginViewModel model)
        {
            // Basic validation
            if (model == null || string.IsNullOrWhiteSpace(model.EmailAddress ?? "") || string.IsNullOrWhiteSpace(model.Password))
            {
                return (SignInResult.Failed, false, "Email and password are required.");
            }

            // Find user by email
            var user = await _userManager.FindByEmailAsync(model.EmailAddress ?? "");

            if (user == null)
            {
                // Don't reveal whether the email exists (security)
                return (SignInResult.Failed, false, "Invalid email or password.");
            }

            // Check if email is confirmed (if your app requires it)
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                return (SignInResult.NotAllowed, false, "Please confirm your email before logging in.");
            }

            // Optional: Check if account is locked or disabled (custom logic)
            if (await _userManager.IsLockedOutAsync(user))
            {
                return (SignInResult.LockedOut, false, "Your account has been locked. Please try again later.");
            }

            // Perform actual sign in
            var result = await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            // Handle different outcomes
            if (result.Succeeded)
            {
                return (result, false, null);
            }

            if (result.RequiresTwoFactor)
            {
                return (result, true, null);
            }

            if (result.IsLockedOut)
            {
                return (result, false, "Your account has been locked due to multiple failed login attempts.");
            }

            if (result.IsNotAllowed)
            {
                return (result, false, "Login is currently not allowed for this account.");
            }

            // Default failure (wrong password, etc.)
            return (SignInResult.Failed, false, "Invalid email or password.");
        }
        public async Task<bool> TwoFactorAuthentication(ApplicationUser user)
        {
            if (user == null || !user.TwoFactorEnabled) return false;

            // Generate the 6-digit token
            var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);

            try
            {
                string subject = "Your 2FA Login Code";
                string message = $"Your security code is: <b>{token}</b>. It expires in 5 minutes.";


                var emailJob = new NotificationJob(user.Email ?? "", subject, message, NotificationType.Email);
                await _notificationChannel.Writer.WriteAsync(emailJob);

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

            // Verify the token
            bool isValid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, token);

            if (!isValid)
            {
                response.Success = false;
                response.Message = "Invalid or expired code.";
                return response;
            }

            // IMPORTANT: Actually log the user in since the token is valid
            await _signInManager.SignInAsync(user, rememberMe);

            response.Success = true;
            response.Data = user;
            return response;
        }
        public bool ForgotPasswordAsync(ApplicationUser user, string callbackUrl)
        {
            // 1. Basic Validation
            if (user == null || string.IsNullOrEmpty(callbackUrl)) return false;

            try
            {
                // 2. Prepare the Content
                string subject = "Reset Your Password";

                // Use a professional HTML template so it doesn't look like spam
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

                // 3. Send via your Email Service
                var emailJob = new NotificationJob(user.Email ?? "", subject, message, NotificationType.Email);


                return true;
            }
            catch
            {
                // Log your error here if you have a logger
                return false;
            }
        }
        public async Task<ServiceResponse<ApplicationUser>> ResetPasswordAsync(ApplicationUser user, string token, string Password)
        {
            var response = new ServiceResponse<ApplicationUser>();

            if (user == null || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(Password))
            {
                response.Success = false;
                response.Message = "Invalid request data.";
                return response;
            }

            try
            {
                // 1. DECODE the token
                // Ensure you use the same Encoding (UTF8) used during the encoding phase
                string decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));

                // 2. Execute the reset
                var result = await _userManager.ResetPasswordAsync(user, decodedToken, Password);

                if (!result.Succeeded)
                {
                    response.Success = false;
                    // It's better to show the actual Identity error for debugging, 
                    // though in production you might want to be vague for security.
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
        public async Task<ServiceResponse<ApplicationUser>> ChangePasswordAsync(string userID, ChangePasswordDTO model)
        {
            var response = new ServiceResponse<ApplicationUser>();
            var user = await _userManager.FindByIdAsync(userID);
            if (user == null || string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(model.NewPassword))
            {
                response.Success = false;
                response.Message = "User Details not Found";
                return response;
            }
            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                response.Success = false;
                response.Message = "Passwoord Change Unsuccesfully";
            }
            response.Data = user;
            response.Success = true;
            response.Message = "Password changed Succesfully";
            return response;

        }
        public async Task<ServiceResponse<ApplicationUser>> LockOutUserAsync(string userId)
        {
            var response = new ServiceResponse<ApplicationUser>();

            // 1. Use the correct search method (FindById, not FindByEmail)
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found.";
                return response;
            }

            // 2. Enable lockout and set the date
            // DateTimeOffset.MaxValue effectively locks them out forever
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

        public async Task<bool> SetTwoFactorAuthentication(ApplicationUser user)
        {
            if (user == null)
            {
                return false;
            }
            if (user.TwoFactorEnabled)
            {
                return true;
            }

            // This is the part that actually updates the database
            var result = await _userManager.SetTwoFactorEnabledAsync(user, true);

            return result.Succeeded;
        }

    }
}



//int attempts = 0;
//string finalprefix = model.Roles.Equals("Öfficial", StringComparison.OrdinalIgnoreCase) ? "OFF-" : "VOT-";
//string generateId = string.Empty;
//bool isUnique = false;

//while (!isUnique)
//{
//    string rawinput = $"{model.FirstName} {model.LastName}_{model.DateOfBirth: yyy-MM-dd}_{model.Roles}_{SystemSalt}_{attempts}";
//    generateId = finalprefix + GenerateSecureId(rawinput);

//    if (model.Roles.Equals("Official", StringComparison.OrdinalIgnoreCase))
//    {
//        isUnique = !await _DbContext.Users.AnyAsync(u => u.OfficialStaffId == generateId);
//    }
//    else
//    {
//        isUnique = !await _DbContext.Users.AnyAsync(u => u.VoterRegistrationID == generateId);


//    }
//    if (!isUnique) attempts++;

//}
