using System.Text.Json;
using BloodSuckersSlot.Shared.Models;

namespace BloodSuckersSlot.Web.Services
{
    public interface IGamingEntityService
    {
        Task<BloodSuckersSlot.Shared.Models.PaginatedResult<GamingEntityListItem>> GetEntitiesAsync(GamingEntityFilter filter);
        Task<List<GamingEntityListItem>> GetHierarchicalEntitiesAsync();
        Task<List<GamingEntityListItem>> GetHierarchicalEntitiesLightAsync();
        Task<GamingEntityDetail?> GetEntityByIdAsync(string id, string? cacheBuster = null);
        Task<GamingEntityHierarchy?> GetEntityHierarchyAsync(string id);
        Task<List<GamingEntityListItem>> GetChildrenAsync(string parentId);
        Task<GamingEntityDetail?> CreateEntityAsync(GamingEntity entity);
        Task<GamingEntityDetail?> UpdateEntityAsync(string id, GamingEntity entity);
        Task<bool> DeleteEntityAsync(string id);
        Task<bool> ToggleActiveAsync(string id);
        Task<List<string>> GetCurrenciesAsync();
        Task<Dictionary<string, int>> GetStatsAsync();
        Task<List<GamingEntityListItem>> GetEntitiesByRoleAsync(EntityRole role);
    }

    public class GamingEntityService : IGamingEntityService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly JsonSerializerOptions _jsonOptions;

        public GamingEntityService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task<BloodSuckersSlot.Shared.Models.PaginatedResult<GamingEntityListItem>> GetEntitiesAsync(GamingEntityFilter filter)
        {
            try
            {
                var queryParams = new List<string>();
                
                if (filter.Role.HasValue)
                    queryParams.Add($"role={filter.Role.Value}");
                
                if (!string.IsNullOrEmpty(filter.SuperAgentId))
                    queryParams.Add($"superAgentId={Uri.EscapeDataString(filter.SuperAgentId)}");
                
                if (!string.IsNullOrEmpty(filter.AgentId))
                    queryParams.Add($"agentId={Uri.EscapeDataString(filter.AgentId)}");
                
                if (!string.IsNullOrEmpty(filter.TokenId))
                    queryParams.Add($"tokenId={Uri.EscapeDataString(filter.TokenId)}");
                
                if (filter.Active.HasValue)
                    queryParams.Add($"active={filter.Active.Value}");
                
                if (!string.IsNullOrEmpty(filter.SearchTerm))
                    queryParams.Add($"searchTerm={Uri.EscapeDataString(filter.SearchTerm)}");
                
                if (!string.IsNullOrEmpty(filter.Currency))
                    queryParams.Add($"currency={Uri.EscapeDataString(filter.Currency)}");
                
                if (filter.MinRtp.HasValue)
                    queryParams.Add($"minRtp={filter.MinRtp.Value}");
                
                if (filter.MaxRtp.HasValue)
                    queryParams.Add($"maxRtp={filter.MaxRtp.Value}");
                
                queryParams.Add($"pageNumber={filter.PageNumber}");
                queryParams.Add($"pageSize={filter.PageSize}");

                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var url = $"{apiBaseUrl}/api/gamingentities?{string.Join("&", queryParams)}";
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<BloodSuckersSlot.Shared.Models.PaginatedResult<GamingEntityListItem>>(json, _jsonOptions);
                    return result ?? new BloodSuckersSlot.Shared.Models.PaginatedResult<GamingEntityListItem>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new BloodSuckersSlot.Shared.Models.PaginatedResult<GamingEntityListItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new BloodSuckersSlot.Shared.Models.PaginatedResult<GamingEntityListItem>();
            }
        }

        public async Task<List<GamingEntityListItem>> GetHierarchicalEntitiesAsync()
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var url = $"{apiBaseUrl}/api/gamingentities/hierarchical";
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<GamingEntityListItem>>(json, _jsonOptions);
                    return result ?? new List<GamingEntityListItem>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new List<GamingEntityListItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new List<GamingEntityListItem>();
            }
        }

        public async Task<List<GamingEntityListItem>> GetHierarchicalEntitiesLightAsync()
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var url = $"{apiBaseUrl}/api/gamingentities/hierarchical-light";
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<GamingEntityListItem>>(json, _jsonOptions);
                    return result ?? new List<GamingEntityListItem>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new List<GamingEntityListItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new List<GamingEntityListItem>();
            }
        }

        public async Task<GamingEntityDetail?> GetEntityByIdAsync(string id, string? cacheBuster = null)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var url = $"{apiBaseUrl}/api/gamingentities/{id}";
                
                // Add cache-busting parameter if provided
                if (!string.IsNullOrEmpty(cacheBuster))
                {
                    url += $"?_t={cacheBuster}";
                }
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<GamingEntityDetail>(json, _jsonOptions);
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return null;
            }
        }

        public async Task<GamingEntityHierarchy?> GetEntityHierarchyAsync(string id)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/gamingentities/{id}/hierarchy");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<GamingEntityHierarchy>(json, _jsonOptions);
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return null;
            }
        }

        public async Task<List<GamingEntityListItem>> GetChildrenAsync(string parentId)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/gamingentities/{parentId}/children");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<GamingEntityListItem>>(json, _jsonOptions);
                    return result ?? new List<GamingEntityListItem>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new List<GamingEntityListItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new List<GamingEntityListItem>();
            }
        }

        public async Task<GamingEntityDetail?> CreateEntityAsync(GamingEntity entity)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var json = JsonSerializer.Serialize(entity, _jsonOptions);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/gamingentities", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<GamingEntityDetail>(responseJson, _jsonOptions);
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return null;
            }
        }

        public async Task<GamingEntityDetail?> UpdateEntityAsync(string id, GamingEntity entity)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var json = JsonSerializer.Serialize(entity, _jsonOptions);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync($"{apiBaseUrl}/api/gamingentities/{id}", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<GamingEntityDetail>(responseJson, _jsonOptions);
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DeleteEntityAsync(string id)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.DeleteAsync($"{apiBaseUrl}/api/gamingentities/{id}");
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.PatchAsync($"{apiBaseUrl}/api/gamingentities/{id}/toggle-active", null);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetCurrenciesAsync()
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/gamingentities/currencies");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
                    return result ?? new List<string>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<Dictionary<string, int>> GetStatsAsync()
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/gamingentities/stats");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, int>>(json, _jsonOptions);
                    return result ?? new Dictionary<string, int>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new Dictionary<string, int>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new Dictionary<string, int>();
            }
        }

        public async Task<List<GamingEntityListItem>> GetEntitiesByRoleAsync(EntityRole role)
        {
            try
            {
                var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "/api";
                var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/gamingentities/by-role/{role}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<GamingEntityListItem>>(json, _jsonOptions);
                    return result ?? new List<GamingEntityListItem>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new List<GamingEntityListItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new List<GamingEntityListItem>();
            }
        }
    }
}
