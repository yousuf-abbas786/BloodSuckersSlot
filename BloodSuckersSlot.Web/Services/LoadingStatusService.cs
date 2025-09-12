using System.Net.Http.Json;

namespace BloodSuckersSlot.Web.Services
{
    public class LoadingStatusService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LoadingStatusService> _logger;
        
        public bool IsFullyLoaded { get; private set; } = false;
        public int LoadingProgress { get; private set; } = 0;
        public int CacheSize { get; private set; } = 0;
        public int TotalReelSetsLoaded { get; private set; } = 0;
        public string Status { get; private set; } = "loading";
        public string Message { get; private set; } = "Service is still loading reel sets";
        
        public event Action? OnLoadingStatusChanged;
        
        public LoadingStatusService(HttpClient httpClient, ILogger<LoadingStatusService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }
        
        public async Task<bool> CheckLoadingStatusAsync()
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<LoadingStatusResponse>("/api/spin/loading-status");
                
                if (response != null)
                {
                    IsFullyLoaded = response.IsFullyLoaded;
                    LoadingProgress = response.Progress;
                    CacheSize = response.CacheSize;
                    TotalReelSetsLoaded = response.TotalReelSetsLoaded;
                    Status = response.Status;
                    Message = response.Message;
                    
                    _logger.LogInformation("🔄 Loading Status: {Status} ({Progress}%) - {TotalSets} sets loaded", 
                        Status, LoadingProgress, TotalReelSetsLoaded);
                    
                    OnLoadingStatusChanged?.Invoke();
                    return IsFullyLoaded;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to check loading status");
                return false;
            }
        }
        
        public async Task StartPollingAsync()
        {
            _logger.LogInformation("🔄 Starting loading status polling...");
            
            while (!IsFullyLoaded)
            {
                await CheckLoadingStatusAsync();
                await Task.Delay(500); // Check every 500ms for better responsiveness
            }
            
            _logger.LogInformation("✅ Loading complete! Service ready for spins");
        }
    }
    
    public class LoadingStatusResponse
    {
        public bool IsFullyLoaded { get; set; }
        public int Progress { get; set; }
        public int CacheSize { get; set; }
        public int TotalReelSetsLoaded { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
