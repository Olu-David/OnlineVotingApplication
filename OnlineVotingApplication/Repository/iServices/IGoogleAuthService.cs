using OnlineVotingApplication.DataTransferView;

namespace OnlineVotingApplication.Repository.iServices
{
    public interface IGoogleAuthService
    {
        Task<GoogleUserInfoDto?> GetGoogleUserInfoAsync(string accessToken);
    }
}
