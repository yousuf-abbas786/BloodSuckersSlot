using System.Text.Json;
using Shared.Models;
using Microsoft.JSInterop;

namespace BloodSuckersSlot.Web.Services
{
    public interface IPlayerSessionService
    {
        Task<PlayerSessionResponse?> StartSessionAsync(decimal initialBalance = 1000);
        Task<PlayerSessionResponse?> GetCurrentSessionAsync();
        Task<bool> EndCurrentSessionAsync();
        Task<bool> UpdateSessionStatsAsync(decimal betAmount, decimal winAmount, bool isWinningSpin, bool isFreeSpin = false, bool isBonusTriggered = false, int freeSpinsAwarded = 0);
        Task<PlayerStatsResponse?> GetPlayerStatsAsync();
        Task<List<PlayerSessionResponse>> GetPlayerSessionsAsync(int pageNumber = 1, int pageSize = 20);
        Task<bool> UpdateActivityAsync();
    }

    public class PlayerSessionService : IPlayerSessionService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IJSRuntime _jsRuntime;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IAuthService _authService;

        public PlayerSessionService(HttpClient httpClient, IConfiguration configuration, IJSRuntime jsRuntime, IAuthService authService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _jsRuntime = jsRuntime;
            _authService = authService;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task<PlayerSessionResponse?> StartSessionAsync(decimal initialBalance = 1000)
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    await _jsRuntime.InvokeVoidAsync("console.error", "No authentication token available");
                    return null;
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var request = new StartSessionRequest
                {
                    InitialBalance = initialBalance
                };

                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/playersession/start", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var session = JsonSerializer.Deserialize<PlayerSessionResponse>(responseJson, _jsonOptions);
                    await _jsRuntime.InvokeVoidAsync("console.log", "Player session started:", session);
                    return session;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    await _jsRuntime.InvokeVoidAsync("console.error", "Failed to start session:", errorContent);
                }
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error starting player session:", ex.Message);
            }

            return null;
        }

        public async Task<PlayerSessionResponse?> GetCurrentSessionAsync()
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    return null;
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/playersession/current");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<PlayerSessionResponse>(json, _jsonOptions);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // No active session found
                    return null;
                }
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error getting current session:", ex.Message);
            }

            return null;
        }

        public async Task<bool> EndCurrentSessionAsync()
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/playersession/end", null);

                if (response.IsSuccessStatusCode)
                {
                    await _jsRuntime.InvokeVoidAsync("console.log", "Player session ended successfully");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    await _jsRuntime.InvokeVoidAsync("console.error", "Failed to end session:", errorContent);
                }
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error ending session:", ex.Message);
            }

            return false;
        }

        public async Task<bool> UpdateSessionStatsAsync(decimal betAmount, decimal winAmount, bool isWinningSpin, bool isFreeSpin = false, bool isBonusTriggered = false, int freeSpinsAwarded = 0)
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var request = new UpdateSessionStatsRequest
                {
                    BetAmount = betAmount,
                    WinAmount = winAmount,
                    IsWinningSpin = isWinningSpin,
                    IsFreeSpin = isFreeSpin,
                    IsBonusTriggered = isBonusTriggered,
                    FreeSpinsAwarded = freeSpinsAwarded,
                    CurrentBalance = 0 // Will be calculated on server
                };

                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/playersession/update-stats", content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    await _jsRuntime.InvokeVoidAsync("console.error", "Failed to update session stats:", errorContent);
                }
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error updating session stats:", ex.Message);
            }

            return false;
        }

        public async Task<PlayerStatsResponse?> GetPlayerStatsAsync()
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    return null;
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/playersession/stats");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<PlayerStatsResponse>(json, _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error getting player stats:", ex.Message);
            }

            return null;
        }

        public async Task<List<PlayerSessionResponse>> GetPlayerSessionsAsync(int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    return new List<PlayerSessionResponse>();
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/playersession/history?pageNumber={pageNumber}&pageSize={pageSize}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<PlayerSessionResponse>>(json, _jsonOptions) ?? new List<PlayerSessionResponse>();
                }
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error getting player sessions:", ex.Message);
            }

            return new List<PlayerSessionResponse>();
        }

        public async Task<bool> UpdateActivityAsync()
        {
            try
            {
                var token = _authService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/playersession/activity", null);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                await _jsRuntime.InvokeVoidAsync("console.error", "Error updating activity:", ex.Message);
            }

            return false;
        }
    }
}
