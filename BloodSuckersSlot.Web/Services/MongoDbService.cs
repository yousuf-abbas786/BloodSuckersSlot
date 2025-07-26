using System.Text.Json;

namespace BloodSuckersSlot.Web.Services
{
    public class ReelSetListItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public double ExpectedRtp { get; set; }
        public double EstimatedHitRate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ReelSetDetail
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public double ExpectedRtp { get; set; }
        public double EstimatedHitRate { get; set; }
        public List<List<string>> Reels { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    public class PaginatedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class MongoDbService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public MongoDbService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            
            // Set the base address to the API
            var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "https://localhost:7001";
            _httpClient.BaseAddress = new Uri(apiBaseUrl);
        }

        public async Task<PaginatedResult<ReelSetListItem>> GetReelSetsAsync(
            int pageNumber = 1, 
            int pageSize = 50, 
            string? tag = null, 
            double? minRtp = null, 
            double? maxRtp = null,
            string? searchTerm = null)
        {
            try
            {
                var queryParams = new List<string>();
                queryParams.Add($"pageNumber={pageNumber}");
                queryParams.Add($"pageSize={pageSize}");
                
                if (!string.IsNullOrEmpty(tag))
                    queryParams.Add($"tag={Uri.EscapeDataString(tag)}");
                
                if (minRtp.HasValue)
                    queryParams.Add($"minRtp={minRtp.Value}");
                
                if (maxRtp.HasValue)
                    queryParams.Add($"maxRtp={maxRtp.Value}");
                
                if (!string.IsNullOrEmpty(searchTerm))
                    queryParams.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");

                var url = $"/api/reelsets?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<PaginatedResult<ReelSetListItem>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return result ?? new PaginatedResult<ReelSetListItem>();
                }
                else
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return new PaginatedResult<ReelSetListItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                return new PaginatedResult<ReelSetListItem>();
            }
        }

        public async Task<ReelSetDetail?> GetReelSetDetailAsync(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/reelsets/{id}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<ReelSetDetail>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return result;
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

        public async Task<List<string>> GetAvailableTagsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/reelsets/tags");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<string>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
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

        public async Task<Dictionary<string, object>> GetStatsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/reelsets/stats");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    // Ensure all required keys exist with default values
                    var safeResult = result ?? new Dictionary<string, object>();
                    if (!safeResult.ContainsKey("TotalCount"))
                        safeResult["TotalCount"] = 0;
                    if (!safeResult.ContainsKey("AvgRtp"))
                        safeResult["AvgRtp"] = 0.0;
                    if (!safeResult.ContainsKey("MinRtp"))
                        safeResult["MinRtp"] = 0.0;
                    if (!safeResult.ContainsKey("MaxRtp"))
                        safeResult["MaxRtp"] = 0.0;
                    if (!safeResult.ContainsKey("TagCounts"))
                        safeResult["TagCounts"] = new Dictionary<string, int>();
                    
                    return safeResult;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"API Error: {response.StatusCode} - {errorContent}");
                    
                    // Return safe default values
                    return new Dictionary<string, object>
                    {
                        ["TotalCount"] = 0,
                        ["AvgRtp"] = 0.0,
                        ["MinRtp"] = 0.0,
                        ["MaxRtp"] = 0.0,
                        ["TagCounts"] = new Dictionary<string, int>()
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling API: {ex.Message}");
                
                // Return safe default values
                return new Dictionary<string, object>
                {
                    ["TotalCount"] = 0,
                    ["AvgRtp"] = 0.0,
                    ["MinRtp"] = 0.0,
                    ["MaxRtp"] = 0.0,
                    ["TagCounts"] = new Dictionary<string, int>()
                };
            }
        }
    }
} 