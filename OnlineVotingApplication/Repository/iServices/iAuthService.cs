using Microsoft.AspNetCore.Identity;
using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iAuthService
    {
     
        //Task<ServiceResponse<ApplicationUser>> RegistrationAsyncAlpha(RegistrationViewModel model, string roles);
        //Task<ServiceResponse<ApplicationUser>> RegisterUser(RegistrationViewModel model);
        Task<ServiceResponse<ApplicationUser>> RegisterUser(RegistrationViewModel model, string assignedRole = "Voter");
        Task<(SignInResult Result, bool RequiresTwoFactor, string? ErrorMessage)> LoginUserAsync(LoginViewModel model);

        Task<ServiceResponse<ApplicationUser>> SendConfirmationTokenAsync(ApplicationUser user, string confirmationLink);
        Task<bool> ConfirmEmailAsync(string userId, string token);

        Task<bool> TwoFactorAuthentication(ApplicationUser user);
        Task<ServiceResponse<ApplicationUser>> ConfirmTwoFactorAsync(string userId, string token, bool rememberMe);
        Task<bool> SetTwoFactorAuthentication(ApplicationUser user);

        Task<bool> ForgotPasswordAsync(ApplicationUser user, string callbackUrl);
        Task<ServiceResponse<ApplicationUser>> ResetPasswordAsync(ApplicationUser user, string token, string password);
        Task<ServiceResponse<ApplicationUser>> ChangePasswordAsync(string userId, ChangePasswordDTO model);

        Task<ServiceResponse<ApplicationUser>> LockOutUserAsync(string userId);
    }
}