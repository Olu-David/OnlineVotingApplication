using OnlineVotingApplication.DataTransferView;
namespace OnlineVotingApplication.Repository.iServices

{
    public interface IAppleAuthService
    {
        Task<AppleTokenResponseDto?> ValidateAuthorizationCodeAsync(string authorizationCode, string clientSecret);
    }
}
