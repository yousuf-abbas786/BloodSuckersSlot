using MongoDB.Driver;
using MongoDB.Bson;
using Shared;
using BloodSuckersSlot.Api.Models;

namespace BloodSuckersSlot.Api.Services
{
    public interface IReelSetCacheService
    {
        Task<List<ReelSet>> GetReelSetsForRtpRangeAsync(double minRtp, double maxRtp, int limit = 1000);
        List<ReelSet> GetInstantReelSets();
        Task PreloadEssentialReelSetsAsync();
        int GetCacheSize();
        int GetTotalReelSetsLoaded();
    }

    public class ReelSetCacheService : IReelSetCacheService
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly ILogger<ReelSetCacheService> _logger;
        private readonly PerformanceSettings _performanceSettings;
        
        // Instance-based caching and performance tracking
        private readonly Dictionary<string, List<ReelSet>> _rtpRangeCache = new();
        private readonly SemaphoreSlim _cacheLock = new(1);
        private int _maxCacheSize = 10000;
        private int _maxReelSetsPerRange = 1000;
        private int _totalReelSetsLoaded = 0;
        
        // Background prefetching
        private readonly Dictionary<string, Task> _prefetchTasks = new();
        private readonly SemaphoreSlim _prefetchLock = new(1);
        private int _prefetchRangeCount = 5;
        private double _prefetchRangeSize = 0.1;
        private DateTime _lastPrefetchTime = DateTime.MinValue;
        private TimeSpan _prefetchInterval = TimeSpan.FromSeconds(30);

        public ReelSetCacheService(IMongoDatabase database, ILogger<ReelSetCacheService> logger, PerformanceSettings performanceSettings)
        {
            var startTime = DateTime.UtcNow;
            
            _logger = logger;
            _performanceSettings = performanceSettings;
            _collection = database.GetCollection<BsonDocument>("reelsets");
            
            // Initialize performance settings from configuration
            _maxCacheSize = _performanceSettings.MaxCacheSize;
            _maxReelSetsPerRange = _performanceSettings.MaxReelSetsPerRange;
            _prefetchRangeCount = _performanceSettings.PrefetchRangeCount;
            _prefetchRangeSize = _performanceSettings.PrefetchRangeSize;
            _prefetchInterval = TimeSpan.FromSeconds(_performanceSettings.PrefetchIntervalSeconds);
            
            var initTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("🚀 ReelSetCacheService initialized in {InitTime:F2}ms - ready for ultra-fast spins", initTime);
            _logger.LogInformation("📊 Performance Settings: MaxCache={MaxCache}, MaxReelSets={MaxReelSets}, PrefetchRanges={PrefetchRanges}", 
                _maxCacheSize, _maxReelSetsPerRange, _prefetchRangeCount);
            
            // Start background preload
            _logger.LogInformation("🚀 Starting background preload in separate task...");
            _ = Task.Run(async () => await PreloadEssentialReelSetsAsync());
        }

        public async Task<List<ReelSet>> GetReelSetsForRtpRangeAsync(double minRtp, double maxRtp, int limit = 1000)
        {
            var startTime = DateTime.UtcNow;
            var cacheKey = $"rtp_{minRtp:F2}_{maxRtp:F2}";
            
            // Check cache first
            if (_rtpRangeCache.ContainsKey(cacheKey))
            {
                var cacheTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogDebug("🎯 CACHE HIT: Found {Count} reel sets for RTP range {MinRtp:F2}-{MaxRtp:F2} in {CacheTime:F2}ms", 
                    _rtpRangeCache[cacheKey].Count, minRtp, maxRtp, cacheTime);
                return _rtpRangeCache[cacheKey];
            }
            
            _logger.LogInformation("🔄 CACHE MISS: Loading reel sets for RTP range {MinRtp:F2}-{MaxRtp:F2} from database", minRtp, maxRtp);
            
            await _cacheLock.WaitAsync();
            try
            {
                // Double-check after lock
                if (_rtpRangeCache.ContainsKey(cacheKey))
                {
                    var cacheTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogDebug("🎯 CACHE HIT (after lock): Found {Count} reel sets for RTP range {MinRtp:F2}-{MaxRtp:F2} in {CacheTime:F2}ms", 
                        _rtpRangeCache[cacheKey].Count, minRtp, maxRtp, cacheTime);
                    return _rtpRangeCache[cacheKey];
                }
                
                // Load only the needed reel sets from DB
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("expectedRtp", minRtp),
                    Builders<BsonDocument>.Filter.Lte("expectedRtp", maxRtp)
                );
                
                var dbStartTime = DateTime.UtcNow;
                var documents = await _collection.Find(filter).Limit(limit).ToListAsync();
                var dbTime = (DateTime.UtcNow - dbStartTime).TotalMilliseconds;
                
                var reelSets = documents.Select(doc => new ReelSet
                {
                    Name = doc.GetValue("name", "").AsString,
                    Reels = doc["reels"].AsBsonArray
                        .Select(col => col.AsBsonArray.Select(s => s.AsString).ToList()).ToList(),
                    ExpectedRtp = doc.GetValue("expectedRtp", 0.0).ToDouble(),
                    EstimatedHitRate = doc.GetValue("estimatedHitRate", 0.0).ToDouble()
                }).ToList();
                
                // Cache management - remove oldest entries if cache is full
                if (_rtpRangeCache.Count >= _maxCacheSize)
                {
                    var oldestKey = _rtpRangeCache.Keys.First();
                    _rtpRangeCache.Remove(oldestKey);
                    _logger.LogInformation("🗑️ CACHE CLEANUP: Removed oldest cache entry '{OldestKey}'", oldestKey);
                }
                
                _rtpRangeCache[cacheKey] = reelSets;
                _totalReelSetsLoaded += reelSets.Count;
                
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("✅ CACHE STORED: {Count} reel sets for RTP range {MinRtp:F2}-{MaxRtp:F2} | DB: {DbTime:F2}ms | Total: {TotalTime:F2}ms | Cache size: {CacheSize}", 
                    reelSets.Count, minRtp, maxRtp, dbTime, totalTime, _rtpRangeCache.Count);
                
                return reelSets;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public List<ReelSet> GetInstantReelSets()
        {
            var startTime = DateTime.UtcNow;
            var allReelSets = new List<ReelSet>();
            
            // Collect all available reel sets from cache (should be pre-loaded)
            foreach (var kvp in _rtpRangeCache)
            {
                allReelSets.AddRange(kvp.Value);
            }
            
            var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogDebug("⚡ INSTANT REEL SETS: Retrieved {Count} reel sets from {CacheSize} cache ranges in {Time:F2}ms", 
                allReelSets.Count, _rtpRangeCache.Count, totalTime);
            
            return allReelSets;
        }

        public async Task PreloadEssentialReelSetsAsync()
        {
            try
            {
                _logger.LogInformation("🚀 PRELOADING ESSENTIAL REEL SETS for ultra-fast spins...");
                var startTime = DateTime.UtcNow;
                
                // Pre-load the most commonly used RTP ranges
                var essentialRanges = new List<(double min, double max)>
                {
                    (0.05, 0.2),   // Very low RTP
                    (0.2, 0.5),    // Low RTP  
                    (0.5, 0.8),    // Medium-low RTP
                    (0.8, 1.2),    // Balanced RTP
                    (1.2, 1.8),    // High RTP
                    (1.8, 3.0)     // Very high RTP
                };
                
                _logger.LogInformation("📊 Preloading {RangeCount} essential RTP ranges with {SetsPerRange} sets each", 
                    essentialRanges.Count, 200);
                
                var tasks = essentialRanges.Select(async range =>
                {
                    var (min, max) = range;
                    var rangeStartTime = DateTime.UtcNow;
                    await GetReelSetsForRtpRangeAsync(min, max, 200); // Load 200 sets per range
                    var rangeTime = (DateTime.UtcNow - rangeStartTime).TotalMilliseconds;
                    _logger.LogDebug("✅ Range {Min:F2}-{Max:F2} loaded in {Time:F2}ms", min, max, rangeTime);
                }).ToArray();
                
                await Task.WhenAll(tasks);
                
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("✅ PRELOAD COMPLETE: {RangeCount} ranges loaded in {TotalTime:F0}ms", 
                    _rtpRangeCache.Count, totalTime);
                _logger.LogInformation("🎯 READY FOR ULTRA-FAST SPINS! Total reel sets cached: {TotalSets}", _totalReelSetsLoaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to preload essential reel sets");
            }
        }

        public int GetCacheSize()
        {
            return _rtpRangeCache.Count;
        }

        public int GetTotalReelSetsLoaded()
        {
            return _totalReelSetsLoaded;
        }
    }
}
