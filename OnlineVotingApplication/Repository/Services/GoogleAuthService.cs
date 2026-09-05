using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Net.Http.Json;
using System.Text.Json;

namespace OnlineVotingApplication.Repository.Services
{
    public class GoogleAuthService : IGoogleAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GoogleAuthService> _logger;

        public GoogleAuthService(HttpClient httpClient, ILogger<GoogleAuthService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<GoogleUserInfoDto?> GetGoogleUserInfoAsync(string accessToken)
        {
            try
            {
                // Google user info endpoint
                var response = await _httpClient.GetAsync($"oauth2/v3/userinfo?access_token={accessToken}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to retrieve Google user info. Status code: {StatusCode}", response.StatusCode);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<GoogleUserInfoDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while calling Google API.");
                return null;
            }
        }
    }

  
}
