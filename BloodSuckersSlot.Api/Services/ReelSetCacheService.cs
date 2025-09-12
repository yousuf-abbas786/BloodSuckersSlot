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
        
        // 🚨 LOADING STATUS METHODS
        bool IsFullyLoaded();
        Task WaitForLoadingCompleteAsync();
        int GetLoadingProgress();
        
        // Dynamic Prefetching Methods
        Task PrefetchRtpRangeAsync(double minRtp, double maxRtp, int priority = 1);
        Task PrefetchBasedOnPlayerTrendsAsync(double currentRtp, double targetRtp, int recentSpins = 10);
        Task StartBackgroundPrefetchingAsync();
        Task StopBackgroundPrefetchingAsync();
        bool IsPrefetchingActive();
        Dictionary<string, object> GetPrefetchStats();
        
        // 🚀 ULTRA-FAST PARALLEL LOADING
        Task<List<ReelSet>> LoadMultipleRtpRangesParallelAsync(List<(double min, double max)> ranges, int limitPerRange = 1000);
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
        
        // 🚨 LOADING STATUS: Block spins until fully loaded
        private volatile bool _isFullyLoaded = false;
        private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);
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
            _logger.LogInformation("🚀 ReelSetCacheService initialized in {InitTime:F2}ms - starting async preload", initTime);
            _logger.LogInformation("📊 Performance Settings: MaxCache={MaxCache}, MaxReelSets={MaxReelSets}, PrefetchRanges={PrefetchRanges}", 
                _maxCacheSize, _maxReelSetsPerRange, _prefetchRangeCount);
            
            // 🚀 ULTRA-FAST MULTI-THREADED PRELOAD: Load all ranges with maximum thread utilization
            _logger.LogInformation("🔄 STARTING ULTRA-FAST MULTI-THREADED PRELOAD - Spins disabled until complete...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await PreloadEssentialReelSetsAsync();
                    _logger.LogInformation("✅ MULTI-THREADED PRELOAD COMPLETE - All reelsets loaded, spins now enabled!");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Multi-threaded preload failed - Service may not be ready");
                }
            });
            
            // Start background prefetching system after preload
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Wait 2 seconds after preload starts
                    await StartBackgroundPrefetchingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Background prefetching failed");
                }
            });
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
                
                // 🚨 DEBUG: Log detailed information about loaded reel sets
                if (reelSets.Any())
                {
                    var loadedMinRtp = reelSets.Min(r => r.ExpectedRtp);
                    var loadedMaxRtp = reelSets.Max(r => r.ExpectedRtp);
                    var loadedAvgRtp = reelSets.Average(r => r.ExpectedRtp);
                    
                    _logger.LogInformation("🔍 DEBUG LOADED REEL SETS: Count={Count}, MinRTP={MinRtp:P2}, MaxRTP={MaxRtp:P2}, AvgRTP={AvgRtp:P2}", 
                        reelSets.Count, loadedMinRtp, loadedMaxRtp, loadedAvgRtp);
                    
                    // Log first 5 reel sets as samples
                    foreach (var set in reelSets.Take(5))
                    {
                        _logger.LogInformation("🔍 SAMPLE REEL SET: Name={Name}, RTP={Rtp:P2}, HitRate={HitRate:P2}", 
                            set.Name, set.ExpectedRtp, set.EstimatedHitRate);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ NO REEL SETS LOADED for range {MinRtp:F2}-{MaxRtp:F2}", minRtp, maxRtp);
                }
                
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
            
            // 🚨 DEBUG: Log comprehensive information about instant reel sets
            if (allReelSets.Any())
            {
                var minRtp = allReelSets.Min(r => r.ExpectedRtp);
                var maxRtp = allReelSets.Max(r => r.ExpectedRtp);
                var avgRtp = allReelSets.Average(r => r.ExpectedRtp);
                var highRtpCount = allReelSets.Count(r => r.ExpectedRtp >= 0.9);
                var ultraHighRtpCount = allReelSets.Count(r => r.ExpectedRtp >= 1.0);
                
                _logger.LogInformation("🔍 DEBUG INSTANT REEL SETS: Total={Total}, MinRTP={MinRtp:P2}, MaxRTP={MaxRtp:P2}, AvgRTP={AvgRtp:P2}", 
                    allReelSets.Count, minRtp, maxRtp, avgRtp);
                _logger.LogInformation("🔍 DEBUG HIGH RTP COUNTS: HighRTP(≥90%)={HighCount}, UltraHighRTP(≥100%)={UltraHighCount}", 
                    highRtpCount, ultraHighRtpCount);
                
                // Log sample reel sets
                foreach (var set in allReelSets.Take(10))
                {
                    _logger.LogInformation("🔍 INSTANT SAMPLE: Name={Name}, RTP={Rtp:P2}, HitRate={HitRate:P2}", 
                        set.Name, set.ExpectedRtp, set.EstimatedHitRate);
                }
            }
            else
            {
                _logger.LogError("🚨 CRITICAL: NO INSTANT REEL SETS AVAILABLE! Cache is empty!");
            }
            
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
                
                // Pre-load the most commonly used RTP ranges with focus on HIGH RTP for 88% target
                var essentialRanges = new List<(double min, double max)>
                {
                    // 🚀 ULTRA-HIGH RTP RANGES (Priority 1 - For recovery above 88% target)
                    (1.20, 1.50),  // Ultra-high RTP (120%-150%) - Emergency recovery
                    (1.10, 1.30),  // Very high RTP (110%-130%) - Aggressive recovery
                    (1.00, 1.20),  // High RTP (100%-120%) - Strong recovery
                    
                    // 🎯 TARGET RTP RANGES (Priority 2 - Around 88% target)
                    (0.90, 1.10),  // Target+ RTP (90%-110%) - Around target
                    (0.85, 1.00),  // Good RTP (85%-100%) - Decent recovery
                    (0.80, 0.95),  // Balanced RTP (80%-95%) - Normal play
                    
                    // 🔄 BALANCED RANGES (Priority 3 - Normal play)
                    (0.70, 0.85),  // Lower RTP (70%-85%) - Reduction mode
                    (0.60, 0.75),  // Low RTP (60%-75%) - Emergency reduction
                    
                    // 🚨 EMERGENCY LOW RANGES (Priority 4 - Only for extreme cases)
                    (0.50, 0.65),  // Very low RTP range
                    (0.40, 0.55),  // Critical low RTP range
                    (0.30, 0.45),  // Ultra low RTP range
                    (0.20, 0.35),  // Disaster low RTP range
                    (0.10, 0.25)   // Ultra disaster low RTP range
                };
                
                _logger.LogInformation("📊 Preloading {RangeCount} essential RTP ranges with {SetsPerRange} sets each", 
                    essentialRanges.Count, 2000);
                
                // 🚀 MAXIMUM THREAD UTILIZATION: Load ALL ranges simultaneously for maximum speed
                _logger.LogInformation("🚀 MAXIMUM THREAD UTILIZATION: Loading ALL {RangeCount} ranges simultaneously", essentialRanges.Count);
                
                // 🚀 MAXIMUM THREAD POOL: Configure for absolute maximum performance
                var processorCount = Environment.ProcessorCount;
                ThreadPool.SetMinThreads(processorCount * 4, processorCount * 4);
                ThreadPool.SetMaxThreads(processorCount * 8, processorCount * 8);
                _logger.LogInformation("🔧 MAXIMUM Thread pool: Min={MinThreads}, Max={MaxThreads} (CPU cores: {Cores})", 
                    processorCount * 4, processorCount * 8, processorCount);
                
                // 🚀 MAXIMUM PARALLELISM: Load ALL ranges simultaneously for maximum speed
                var loadingTasks = essentialRanges.Select(async range =>
                {
                    var (min, max) = range;
                    var rangeStartTime = DateTime.UtcNow;
                    
                    try
                    {
                        // 🚀 HIGH LIMITS: Use maximum limits for all ranges
                        var limit = 2500; // High limit for all ranges
                        await GetReelSetsForRtpRangeAsync(min, max, limit);
                        
                        var rangeTime = (DateTime.UtcNow - rangeStartTime).TotalMilliseconds;
                        _logger.LogInformation("✅ RANGE LOADED: {Min:F2}-{Max:F2} ({Limit} sets) in {Time:F2}ms", 
                            min, max, limit, rangeTime);
                        
                        return true;
                    }
                    catch (Exception ex)
                    {
                        var rangeTime = (DateTime.UtcNow - rangeStartTime).TotalMilliseconds;
                        _logger.LogError(ex, "❌ FAILED RANGE: {Min:F2}-{Max:F2} after {Time:F2}ms", min, max, rangeTime);
                        return false;
                    }
                }).ToArray();
                
                // Wait for ALL ranges to complete simultaneously
                var allResults = await Task.WhenAll(loadingTasks);
                var successfulRanges = allResults.Count(r => r);
                
                _logger.LogInformation("✅ MAXIMUM PARALLELISM COMPLETE: {SuccessfulRanges}/{TotalRanges} ranges loaded successfully", 
                    successfulRanges, essentialRanges.Count);
                
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("✅ PRELOAD COMPLETE: {RangeCount} ranges loaded in {TotalTime:F0}ms", 
                    _rtpRangeCache.Count, totalTime);
                _logger.LogInformation("🎯 READY FOR ULTRA-FAST SPINS! Total reel sets cached: {TotalSets} (28,000 target)", _totalReelSetsLoaded);
                
                // 🚨 SET LOADING COMPLETE: Allow spins to proceed
                _isFullyLoaded = true;
                _logger.LogInformation("🚀 LOADING COMPLETE: Spins are now enabled! Total sets: {TotalSets}", _totalReelSetsLoaded);
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
        
        // 🚨 LOADING STATUS METHODS
        public bool IsFullyLoaded()
        {
            return _isFullyLoaded;
        }
        
        public async Task WaitForLoadingCompleteAsync()
        {
            await _loadingSemaphore.WaitAsync();
            try
            {
                while (!_isFullyLoaded)
                {
                    await Task.Delay(100); // Check every 100ms
                }
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }
        
        public int GetLoadingProgress()
        {
            var totalExpectedRanges = 13; // Based on our essential ranges
            var loadedRanges = _rtpRangeCache.Count;
            return (int)((double)loadedRanges / totalExpectedRanges * 100);
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

        // 🚀 ULTRA-FAST PARALLEL LOADING: Load multiple ranges simultaneously
        public async Task<List<ReelSet>> LoadMultipleRtpRangesParallelAsync(List<(double min, double max)> ranges, int limitPerRange = 1000)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("🚀 ULTRA-FAST PARALLEL LOADING: Loading {RangeCount} ranges with {LimitPerRange} sets each", 
                ranges.Count, limitPerRange);
            
            // 🚀 MAXIMUM PARALLELISM: Load all ranges simultaneously
            var loadingTasks = ranges.Select(async range =>
            {
                var (min, max) = range;
                var rangeStartTime = DateTime.UtcNow;
                
                try
                {
                    var reelSets = await GetReelSetsForRtpRangeAsync(min, max, limitPerRange);
                    var rangeTime = (DateTime.UtcNow - rangeStartTime).TotalMilliseconds;
                    
                    _logger.LogDebug("✅ PARALLEL RANGE: {Min:F2}-{Max:F2} ({Count} sets) in {Time:F2}ms", 
                        min, max, reelSets.Count, rangeTime);
                    
                    return reelSets;
                }
                catch (Exception ex)
                {
                    var rangeTime = (DateTime.UtcNow - rangeStartTime).TotalMilliseconds;
                    _logger.LogError(ex, "❌ PARALLEL RANGE FAILED: {Min:F2}-{Max:F2} after {Time:F2}ms", min, max, rangeTime);
                    return new List<ReelSet>();
                }
            }).ToArray();
            
            // Wait for all ranges to complete
            var allResults = await Task.WhenAll(loadingTasks);
            var totalReelSets = allResults.SelectMany(r => r).ToList();
            
            var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("✅ PARALLEL LOADING COMPLETE: {TotalSets} reel sets from {RangeCount} ranges in {TotalTime:F2}ms", 
                totalReelSets.Count, ranges.Count, totalTime);
            
            return totalReelSets;
        }

        #endregion
    }
}
