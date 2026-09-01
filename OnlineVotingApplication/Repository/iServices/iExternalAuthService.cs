using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iExternalAuthService
    {
        /// <summary>
        /// Exchanges a Google OAuth2 Identity Token for a localized Application User system profile.
        /// </summary>
        Task<ServiceResponse<ApplicationUser>> AuthenticateGoogleUserAsync(string idToken, string assignedRole);

        /// <summary>
        /// Exchanges an Apple Sign-In Identity Token for a localized Application User system profile.
        /// </summary>
        Task<ServiceResponse<ApplicationUser>> AuthenticateAppleUserAsync(string idToken, string firstName, string lastName, string assignedRole);
    }
}
