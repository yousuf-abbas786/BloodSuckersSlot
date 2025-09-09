using System.Text.Json;
using BloodSuckersSlot.Shared.Models;
using Microsoft.JSInterop;

namespace BloodSuckersSlot.Web.Services
{
    public interface IAuthService
    {
        Task<EntityLoginResponse?> LoginAsync(string username, string password);
        Task LogoutAsync();
        Task<EntityInfo?> GetCurrentEntityAsync();
        Task<bool> IsAuthenticatedAsync();
        Task<bool> IsAdminAsync();
        Task<bool> IsPlayerAsync();
        Task<string?> GetEntityGroupIdAsync();
        string? GetToken();
        void SetToken(string token);
    }

    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IJSRuntime _jsRuntime;
        private readonly JsonSerializerOptions _jsonOptions;
        private string? _token;

        public AuthService(HttpClient httpClient, IConfiguration configuration, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _jsRuntime = jsRuntime;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task<EntityLoginResponse?> LoginAsync(string username, string password)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var loginRequest = new EntityLoginRequest
                {
                    Username = username,
                    Password = password
                };

                var json = JsonSerializer.Serialize(loginRequest, _jsonOptions);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                Console.WriteLine($"Attempting login to: {apiBaseUrl}/api/auth/login");
                var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth/login", content);

                Console.WriteLine($"Response status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response content: {responseJson}");
                    var loginResponse = JsonSerializer.Deserialize<EntityLoginResponse>(responseJson, _jsonOptions);
                    
                    if (loginResponse?.Success == true && !string.IsNullOrEmpty(loginResponse.Token))
                    {
                        Console.WriteLine("Login successful, setting token");
                        SetToken(loginResponse.Token);
                        SaveTokenToStorage(loginResponse.Token);
                        return loginResponse;
                    }
                    else
                    {
                        Console.WriteLine($"Login failed: Success={loginResponse?.Success}, Token={!string.IsNullOrEmpty(loginResponse?.Token)}");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"API Error: {response.StatusCode} - {errorContent}");
                }

                return new EntityLoginResponse
                {
                    Success = false,
                    ErrorMessage = $"Login failed - Status: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during login: {ex.Message}");
                return new EntityLoginResponse
                {
                    Success = false,
                    ErrorMessage = "Login failed due to network error"
                };
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                await _httpClient.PostAsync($"{apiBaseUrl}/api/auth/logout", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during logout: {ex.Message}");
            }
            finally
            {
                ClearToken();
                RemoveTokenFromStorage();
            }
        }

        public async Task<EntityInfo?> GetCurrentEntityAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    return null;
                }

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/auth/me");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<EntityInfo>(json, _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting current entity: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            try
            {
                // Load token from localStorage if not already loaded
                if (string.IsNullOrEmpty(_token))
                {
                    await LoadTokenFromStorage();
                }
                
                Console.WriteLine($"Checking authentication. Token exists: {!string.IsNullOrEmpty(_token)}");
                if (string.IsNullOrEmpty(_token))
                {
                    Console.WriteLine("No token found, not authenticated");
                    return false;
                }

                var entity = await GetCurrentEntityAsync();
                var isAuthenticated = entity != null;
                Console.WriteLine($"Authentication check result: {isAuthenticated}");
                return isAuthenticated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking authentication: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsAdminAsync()
        {
            var entity = await GetCurrentEntityAsync();
            return entity?.Role == EntityRole.ADMIN;
        }

        public async Task<bool> IsPlayerAsync()
        {
            var entity = await GetCurrentEntityAsync();
            return entity?.Role == EntityRole.PLAYER;
        }

        public async Task<string?> GetEntityGroupIdAsync()
        {
            var entity = await GetCurrentEntityAsync();
            return entity?.GroupId;
        }

        public string? GetToken()
        {
            return _token;
        }

        public void SetToken(string token)
        {
            _token = token;
            // Don't set headers automatically - let individual services handle this
        }

        private void ClearToken()
        {
            _token = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        private async Task LoadTokenFromStorage()
        {
            try
            {
                var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
                if (!string.IsNullOrEmpty(token))
                {
                    _token = token;
                    // Don't set headers automatically - let individual services handle this
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading token from storage: {ex.Message}");
            }
        }

        private async void SaveTokenToStorage(string token)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving token to storage: {ex.Message}");
            }
        }

        private async void RemoveTokenFromStorage()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing token from storage: {ex.Message}");
            }
        }
    }
}
