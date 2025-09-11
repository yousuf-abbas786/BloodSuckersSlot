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
        
        // Dynamic Prefetching Methods
        Task PrefetchRtpRangeAsync(double minRtp, double maxRtp, int priority = 1);
        Task PrefetchBasedOnPlayerTrendsAsync(double currentRtp, double targetRtp, int recentSpins = 10);
        Task StartBackgroundPrefetchingAsync();
        Task StopBackgroundPrefetchingAsync();
        bool IsPrefetchingActive();
        Dictionary<string, object> GetPrefetchStats();
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
        
        // Enhanced prefetching system
        private CancellationTokenSource _prefetchCancellationToken = new();
        private Task _backgroundPrefetchTask = null;
        private bool _isPrefetchingActive = false;
        private readonly Dictionary<string, DateTime> _prefetchHistory = new();
        private readonly Dictionary<string, int> _prefetchPriority = new();
        private readonly Queue<(double minRtp, double maxRtp, int priority)> _prefetchQueue = new();
        private readonly SemaphoreSlim _prefetchQueueLock = new(1);

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
            
            // Start dynamic prefetching system
            _ = Task.Run(async () => await StartBackgroundPrefetchingAsync());
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
                    essentialRanges.Count, 1667);
                
                var tasks = essentialRanges.Select(async range =>
                {
                    var (min, max) = range;
                    var rangeStartTime = DateTime.UtcNow;
                    await GetReelSetsForRtpRangeAsync(min, max, 1667); // Load 1667 sets per range (10,000 total)
                    var rangeTime = (DateTime.UtcNow - rangeStartTime).TotalMilliseconds;
                    _logger.LogDebug("✅ Range {Min:F2}-{Max:F2} loaded in {Time:F2}ms", min, max, rangeTime);
                }).ToArray();
                
                await Task.WhenAll(tasks);
                
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("✅ PRELOAD COMPLETE: {RangeCount} ranges loaded in {TotalTime:F0}ms", 
                    _rtpRangeCache.Count, totalTime);
                _logger.LogInformation("🎯 READY FOR ULTRA-FAST SPINS! Total reel sets cached: {TotalSets} (10,000 target)", _totalReelSetsLoaded);
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

        #region Dynamic Prefetching Methods

        public async Task PrefetchRtpRangeAsync(double minRtp, double maxRtp, int priority = 1)
        {
            var cacheKey = $"rtp_{minRtp:F2}_{maxRtp:F2}";
            
            // Skip if already cached or recently prefetched
            if (_rtpRangeCache.ContainsKey(cacheKey))
            {
                _logger.LogDebug("🎯 PREFETCH SKIP: Range {MinRtp:F2}-{MaxRtp:F2} already cached", minRtp, maxRtp);
                return;
            }

            if (_prefetchHistory.ContainsKey(cacheKey) && 
                DateTime.UtcNow - _prefetchHistory[cacheKey] < TimeSpan.FromMinutes(5))
            {
                _logger.LogDebug("🎯 PREFETCH SKIP: Range {MinRtp:F2}-{MaxRtp:F2} recently prefetched", minRtp, maxRtp);
                return;
            }

            try
            {
                _logger.LogInformation("🚀 PREFETCHING: Range {MinRtp:F2}-{MaxRtp:F2} (Priority: {Priority})", minRtp, maxRtp, priority);
                
                var prefetchStartTime = DateTime.UtcNow;
                await GetReelSetsForRtpRangeAsync(minRtp, maxRtp, _maxReelSetsPerRange);
                var prefetchTime = (DateTime.UtcNow - prefetchStartTime).TotalMilliseconds;
                
                _prefetchHistory[cacheKey] = DateTime.UtcNow;
                _prefetchPriority[cacheKey] = priority;
                
                _logger.LogInformation("✅ PREFETCH COMPLETE: Range {MinRtp:F2}-{MaxRtp:F2} in {Time:F2}ms", 
                    minRtp, maxRtp, prefetchTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PREFETCH FAILED: Range {MinRtp:F2}-{MaxRtp:F2}", minRtp, maxRtp);
            }
        }

        public async Task PrefetchBasedOnPlayerTrendsAsync(double currentRtp, double targetRtp, int recentSpins = 10)
        {
            try
            {
                _logger.LogInformation("🎯 SMART PREFETCH: Current RTP={CurrentRtp:P2}, Target={TargetRtp:P2}, RecentSpins={RecentSpins}", 
                    currentRtp, targetRtp, recentSpins);

                var prefetchTasks = new List<Task>();

                // 1. Prefetch current RTP range (high priority)
                var currentRange = CalculateRtpRange(currentRtp, _prefetchRangeSize);
                prefetchTasks.Add(PrefetchRtpRangeAsync(currentRange.min, currentRange.max, 3));

                // 2. Prefetch target RTP range (high priority)
                var targetRange = CalculateRtpRange(targetRtp, _prefetchRangeSize);
                prefetchTasks.Add(PrefetchRtpRangeAsync(targetRange.min, targetRange.max, 3));

                // 3. Prefetch recovery ranges if RTP is low
                if (currentRtp < targetRtp * 0.8) // If RTP is significantly below target
                {
                    var recoveryRanges = CalculateRecoveryRanges(currentRtp, targetRtp);
                    foreach (var range in recoveryRanges)
                    {
                        prefetchTasks.Add(PrefetchRtpRangeAsync(range.min, range.max, 2));
                    }
                }

                // 4. Prefetch adjacent ranges for smooth transitions
                var adjacentRanges = CalculateAdjacentRanges(currentRtp, targetRtp);
                foreach (var range in adjacentRanges)
                {
                    prefetchTasks.Add(PrefetchRtpRangeAsync(range.min, range.max, 1));
                }

                await Task.WhenAll(prefetchTasks);
                _logger.LogInformation("✅ SMART PREFETCH COMPLETE: {TaskCount} ranges prefetched", prefetchTasks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SMART PREFETCH FAILED");
            }
        }

        public async Task StartBackgroundPrefetchingAsync()
        {
            if (_isPrefetchingActive)
            {
                _logger.LogWarning("⚠️ Background prefetching already active");
                return;
            }

            _isPrefetchingActive = true;
            _prefetchCancellationToken = new CancellationTokenSource();
            
            _logger.LogInformation("🚀 Starting background prefetching system...");
            
            _backgroundPrefetchTask = Task.Run(async () =>
            {
                while (!_prefetchCancellationToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPrefetchQueueAsync();
                        await Task.Delay(_prefetchInterval, _prefetchCancellationToken.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Background prefetching error");
                        await Task.Delay(TimeSpan.FromSeconds(10), _prefetchCancellationToken.Token);
                    }
                }
            }, _prefetchCancellationToken.Token);
        }

        public async Task StopBackgroundPrefetchingAsync()
        {
            if (!_isPrefetchingActive)
            {
                return;
            }

            _logger.LogInformation("🛑 Stopping background prefetching...");
            _isPrefetchingActive = false;
            _prefetchCancellationToken.Cancel();

            if (_backgroundPrefetchTask != null)
            {
                await _backgroundPrefetchTask;
            }

            _logger.LogInformation("✅ Background prefetching stopped");
        }

        public bool IsPrefetchingActive()
        {
            return _isPrefetchingActive;
        }

        public Dictionary<string, object> GetPrefetchStats()
        {
            return new Dictionary<string, object>
            {
                ["isActive"] = _isPrefetchingActive,
                ["queueSize"] = _prefetchQueue.Count,
                ["historyCount"] = _prefetchHistory.Count,
                ["lastPrefetchTime"] = _lastPrefetchTime,
                ["prefetchInterval"] = _prefetchInterval.TotalSeconds,
                ["cacheSize"] = _rtpRangeCache.Count,
                ["totalReelSetsLoaded"] = _totalReelSetsLoaded
            };
        }

        private async Task ProcessPrefetchQueueAsync()
        {
            await _prefetchQueueLock.WaitAsync();
            try
            {
                if (_prefetchQueue.Count == 0)
                {
                    return;
                }

                // Process up to 3 prefetch requests per cycle
                var tasksToProcess = Math.Min(3, _prefetchQueue.Count);
                var tasks = new List<Task>();

                for (int i = 0; i < tasksToProcess; i++)
                {
                    if (_prefetchQueue.Count > 0)
                    {
                        var (minRtp, maxRtp, priority) = _prefetchQueue.Dequeue();
                        tasks.Add(PrefetchRtpRangeAsync(minRtp, maxRtp, priority));
                    }
                }

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                    _lastPrefetchTime = DateTime.UtcNow;
                }
            }
            finally
            {
                _prefetchQueueLock.Release();
            }
        }

        private (double min, double max) CalculateRtpRange(double centerRtp, double rangeSize)
        {
            var halfRange = rangeSize / 2;
            return (Math.Max(0.01, centerRtp - halfRange), centerRtp + halfRange);
        }

        private List<(double min, double max)> CalculateRecoveryRanges(double currentRtp, double targetRtp)
        {
            var ranges = new List<(double min, double max)>();
            
            // Calculate ranges between current and target RTP
            var step = (targetRtp - currentRtp) / 3;
            for (int i = 1; i <= 3; i++)
            {
                var centerRtp = currentRtp + (step * i);
                ranges.Add(CalculateRtpRange(centerRtp, _prefetchRangeSize));
            }
            
            return ranges;
        }

        private List<(double min, double max)> CalculateAdjacentRanges(double currentRtp, double targetRtp)
        {
            var ranges = new List<(double min, double max)>();
            
            // Add ranges slightly above and below current RTP
            ranges.Add(CalculateRtpRange(currentRtp + _prefetchRangeSize, _prefetchRangeSize));
            ranges.Add(CalculateRtpRange(currentRtp - _prefetchRangeSize, _prefetchRangeSize));
            
            // Add ranges around target RTP
            ranges.Add(CalculateRtpRange(targetRtp + _prefetchRangeSize, _prefetchRangeSize));
            ranges.Add(CalculateRtpRange(targetRtp - _prefetchRangeSize, _prefetchRangeSize));
            
            return ranges;
        }

        #endregion
    }
}
