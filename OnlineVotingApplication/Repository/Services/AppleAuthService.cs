using OnlineVotingApplication.DataTransferView;
using OnlineVotingApplication.Repository.iServices;
using System.Net.Http.Json;

public class AppleAuthService : IAppleAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppleAuthService> _logger;

    public AppleAuthService(HttpClient httpClient, IConfiguration configuration, ILogger<AppleAuthService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AppleTokenResponseDto?> ValidateAuthorizationCodeAsync(string authorizationCode, string clientSecret)
    {
        try
        {
            var clientId = _configuration["Authentication:Apple:ClientId"]; // e.g., com.yourdomain.app

            var formParameters = new Dictionary<string, string>
            {
                { "client_id", clientId ?? string.Empty },
                { "client_secret", clientSecret }, // Signed JWT generated via your private .p8 key
                { "code", authorizationCode },
                { "grant_type", "authorization_code" }
            };

            var content = new FormUrlEncodedContent(formParameters);
            var response = await _httpClient.PostAsync("auth/token", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to validate Apple code. Response: {Error}", errorContent);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AppleTokenResponseDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while calling Apple Auth API.");
            return null;
        }
    }
}


