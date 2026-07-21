using Microsoft.AspNetCore.Identity;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iAuthService
    {
        Task<ServiceResponse<ApplicationUser>> RegistrationAsyncAlpha(RegistrationViewModel model, string Roles);
        Task<ServiceResponse<ApplicationUser>> RegisterUser(RegistrationViewModel model);
        Task<ServiceResponse<ApplicationUser>> ConfirmTwoFactorAsync(string userId, string token, bool rememberMe);
        Task<(SignInResult Result, bool RequiresTwoFactor, string? ErrorMessage)> LoginUserAsync(LoginViewModel model);
        Task<bool> ConfirmEmailAsync(string UserId, string token);
        Task<ServiceResponse<ApplicationUser>> SendConfirmationTokenAsync(ApplicationUser user, string confirmationLink);
        Task<bool> TwoFactorAuthentication(ApplicationUser user);
        bool ForgotPasswordAsync(ApplicationUser user, string callbackUrl);
        Task<ServiceResponse<ApplicationUser>> ResetPasswordAsync(ApplicationUser user, string token, string Password);
        Task<ServiceResponse<ApplicationUser>> ChangePasswordAsync(string userID, ChangePasswordDTO model);
        Task<ServiceResponse<ApplicationUser>> LockOutUserAsync(string userId);
        Task<bool> SetTwoFactorAuthentication(ApplicationUser user);

    }
}
