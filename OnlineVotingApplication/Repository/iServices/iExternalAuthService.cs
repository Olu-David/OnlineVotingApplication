using OnlineVotingApplication.Areas.Identity.Data;
using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Models;
using System.Threading.Tasks;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface iExternalAuthService
    {
        Task<ServiceResponse<ApplicationUser>> AuthenticateGoogleUserAsync(string idToken, string? phoneNumber = null);

        Task<ServiceResponse<ApplicationUser>> AuthenticateAppleUserAsync(string idToken, string firstName, string lastName, string? phoneNumber = null);
    }
}
