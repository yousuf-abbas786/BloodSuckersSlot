using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using MongoDB.Bson;
using Shared;
using System.Text.Json;
using System.Diagnostics;

namespace BloodSuckersSlot.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpinController : ControllerBase
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly ILogger<SpinController> _logger;
        private static GameConfig _config;
        
        // Lazy loading with RTP range caching
        private static readonly Dictionary<string, List<ReelSet>> _rtpRangeCache = new();
        private static readonly SemaphoreSlim _cacheLock = new(1);
        private static readonly int _maxCacheSize = 10000; // Limit cache size
        private static readonly int _maxReelSetsPerRange = 1000; // Limit reel sets per range
        
        // Memory monitoring
        private static long _totalMemoryUsed = 0;
        private static int _totalReelSetsLoaded = 0;
        
        // 🚀 SPIN SPEED OPTIMIZATIONS
        private static readonly Dictionary<string, Task> _prefetchTasks = new();
        private static readonly SemaphoreSlim _prefetchLock = new(1);
        private static readonly int _prefetchRangeCount = 5; // Prefetch 5 RTP ranges
        private static readonly double _prefetchRangeSize = 0.1; // 10% RTP range size
        private static DateTime _lastPrefetchTime = DateTime.MinValue;
        private static readonly TimeSpan _prefetchInterval = TimeSpan.FromSeconds(30); // Prefetch every 30 seconds

        public SpinController(IConfiguration configuration, ILogger<SpinController> logger)
        {
            _logger = logger;
            _config = GameConfigLoader.LoadFromConfiguration(configuration);
            
            try
            {
                var connectionString = configuration["MongoDb:ConnectionString"];
                var dbName = configuration["MongoDb:Database"];
                
                _logger.LogInformation($"Connecting to MongoDB: {dbName} at {connectionString?.Split('@').LastOrDefault() ?? "unknown"}");
                
                var client = new MongoClient(connectionString);
                var db = client.GetDatabase(dbName);
                _collection = db.GetCollection<BsonDocument>("reelsets");
                
                _logger.LogInformation("✅ MongoDB connection established successfully");
                
                // DISABLED: Old loading mechanism that loads all 100K reel sets
                // _ = LoadAllReelSetsAsync();
                
                // ENABLED: Lazy loading - only load reel sets when needed
                _logger.LogInformation("🚀 LAZY LOADING ENABLED - No reel sets loaded at startup");
                _logger.LogInformation("💾 MEMORY USAGE: Starting with minimal memory footprint");
                
                // Create indexes for optimal performance
                CreateIndexesAsync().Wait();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ MongoDB connection failed - API cannot start without MongoDB");
                throw new InvalidOperationException($"MongoDB connection failed: {ex.Message}", ex);
            }
        }

        // REMOVED: Old LoadAllReelSetsAsync method - replaced with lazy loading
        // This method was loading all 100K reel sets into memory, causing 23+ GB usage

        private async Task CreateIndexesAsync()
        {
            try
            {
                var indexKeys = new List<CreateIndexModel<BsonDocument>>
                {
                    new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("expectedRtp")),
                    new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("estimatedHitRate")),
                    new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("tag")),
                    // Compound index for efficient RTP range queries
                    new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Combine(
                        Builders<BsonDocument>.IndexKeys.Ascending("expectedRtp"),
                        Builders<BsonDocument>.IndexKeys.Ascending("estimatedHitRate")
                    )),
                    // Compound index for efficient filtering
                    new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Combine(
                        Builders<BsonDocument>.IndexKeys.Ascending("tag"),
                        Builders<BsonDocument>.IndexKeys.Ascending("expectedRtp")
                    ))
                };
                
                await _collection.Indexes.CreateManyAsync(indexKeys);
                _logger.LogInformation("✅ Database indexes created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to create indexes (may already exist)");
            }
        }

        // Lazy loading with RTP range caching
        public async Task<List<ReelSet>> GetReelSetsForRtpRangeAsync(double minRtp, double maxRtp, int limit = 1000)
        {
            var cacheKey = $"rtp_{minRtp:F2}_{maxRtp:F2}";
            
            // Check cache first
            if (_rtpRangeCache.ContainsKey(cacheKey))
            {
                _logger.LogInformation($"🎯 CACHE HIT: Found {_rtpRangeCache[cacheKey].Count} reel sets for RTP range {minRtp:F2}-{maxRtp:F2}");
                return _rtpRangeCache[cacheKey];
            }
            
            await _cacheLock.WaitAsync();
            try
            {
                // Double-check after lock
                if (_rtpRangeCache.ContainsKey(cacheKey))
                {
                    _logger.LogInformation($"🎯 CACHE HIT (after lock): Found {_rtpRangeCache[cacheKey].Count} reel sets for RTP range {minRtp:F2}-{maxRtp:F2}");
                    return _rtpRangeCache[cacheKey];
                }
                
                _logger.LogInformation($"🔄 CACHE MISS: Loading reel sets for RTP range {minRtp:F2}-{maxRtp:F2} from database");
                
                // Load only the needed reel sets from DB
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("expectedRtp", minRtp),
                    Builders<BsonDocument>.Filter.Lte("expectedRtp", maxRtp)
                );
                
                var documents = await _collection.Find(filter).Limit(limit).ToListAsync();
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
                    _logger.LogInformation($"🗑️ CACHE CLEANUP: Removed oldest cache entry '{oldestKey}'");
                }
                
                _rtpRangeCache[cacheKey] = reelSets;
                
                var memoryUsage = GC.GetTotalMemory(false);
                _totalReelSetsLoaded += reelSets.Count;
                _totalMemoryUsed = memoryUsage;
                
                _logger.LogInformation($"✅ CACHE STORED: {reelSets.Count} reel sets for RTP range {minRtp:F2}-{maxRtp:F2} | Cache size: {_rtpRangeCache.Count} | Memory: {memoryUsage / (1024 * 1024):N0} MB");
                
                return reelSets;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        // Get reel sets for current RTP needs - OPTIMIZED FOR SPEED AND VARIETY
        public async Task<List<ReelSet>> GetReelSetsForCurrentRtpAsync(double currentRtp, double targetRtp, GameConfig config)
        {
            var startTime = DateTime.UtcNow;
            var needs = new List<(double min, double max)>();
            
            // 🎯 ENHANCED RTP RANGE SELECTION for better wave patterns
            // Add more variety and wider ranges to create natural RTP waves
            
            if (currentRtp < targetRtp * 0.90)
            {
                // Need higher RTP - load wider ranges for more variety
                needs.Add((currentRtp, targetRtp * 1.3));
                needs.Add((targetRtp * 1.3, targetRtp * 2.5));
                needs.Add((targetRtp * 2.5, targetRtp * 4.0)); // Extreme high RTP for variety
            }
            else if (currentRtp > targetRtp * 1.10)
            {
                // Need lower RTP - load wider ranges for more variety
                needs.Add((targetRtp * 0.3, currentRtp));
                needs.Add((targetRtp * 0.1, targetRtp * 0.3));
                needs.Add((0.05, targetRtp * 0.1)); // Extreme low RTP for variety
            }
            else
            {
                // Near target - load balanced ranges with more variety
                needs.Add((targetRtp * 0.7, targetRtp * 1.3));
                needs.Add((targetRtp * 0.5, targetRtp * 0.7)); // Lower variety
                needs.Add((targetRtp * 1.3, targetRtp * 1.8)); // Higher variety
            }
            
            // 🚀 PARALLEL LOADING for speed
            var tasks = needs.Select(async (range) => 
            {
                var (min, max) = range;
                return await GetReelSetsForRtpRangeAsync(min, max, _maxReelSetsPerRange);
            }).ToArray();
            
            // Wait for all parallel tasks to complete
            var results = await Task.WhenAll(tasks);
            
            var allReelSets = results.SelectMany(x => x).ToList();
            var loadTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            _logger.LogInformation($"🎯 LOADED {allReelSets.Count} reel sets for current RTP {currentRtp:F2} (target: {targetRtp:F2}) in {loadTime:F0}ms");
            
            // Trigger background prefetching for next spins
            _ = Task.Run(async () => await TriggerPrefetchAsync(currentRtp, targetRtp));
            
            return allReelSets;
        }

        // 🚀 BACKGROUND PREFETCHING for faster subsequent spins
        private async Task TriggerPrefetchAsync(double currentRtp, double targetRtp)
        {
            try
            {
                await _prefetchLock.WaitAsync();
                
                // Check if we should prefetch (avoid too frequent prefetching)
                if (DateTime.UtcNow - _lastPrefetchTime < _prefetchInterval)
                {
                    return;
                }
                
                _lastPrefetchTime = DateTime.UtcNow;
                
                // Calculate likely RTP ranges for next spins
                var prefetchRanges = new List<(double min, double max)>();
                
                // 🎯 ENHANCED PREFETCHING for better wave patterns
                // Prefetch wider ranges to ensure variety in future spins
                var rangeSize = targetRtp * _prefetchRangeSize * 1.5; // Increase range size for more variety
                for (int i = 0; i < _prefetchRangeCount; i++)
                {
                    var center = currentRtp + (i - _prefetchRangeCount / 2) * rangeSize;
                    var min = Math.Max(0.05, center - rangeSize / 2); // Allow lower RTP
                    var max = Math.Min(5.0, center + rangeSize / 2);  // Allow higher RTP
                    prefetchRanges.Add((min, max));
                }
                
                // Add extreme ranges for maximum variety
                prefetchRanges.Add((0.05, targetRtp * 0.3));  // Very low RTP
                prefetchRanges.Add((targetRtp * 2.0, 5.0));   // Very high RTP
                
                _logger.LogInformation($"🚀 STARTING PREFETCH: {prefetchRanges.Count} ranges around RTP {currentRtp:F2}");
                
                // Start prefetching in background
                foreach (var (min, max) in prefetchRanges)
                {
                    var cacheKey = $"rtp_{min:F2}_{max:F2}";
                    
                    // Only prefetch if not already cached
                    if (!_rtpRangeCache.ContainsKey(cacheKey) && !_prefetchTasks.ContainsKey(cacheKey))
                    {
                        var prefetchTask = Task.Run(async () =>
                        {
                            try
                            {
                                await GetReelSetsForRtpRangeAsync(min, max, 500); // Smaller limit for prefetch
                                _logger.LogInformation($"✅ PREFETCH COMPLETED: RTP range {min:F2}-{max:F2}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"⚠️ PREFETCH FAILED: RTP range {min:F2}-{max:F2}");
                            }
                            finally
                            {
                                _prefetchTasks.Remove(cacheKey);
                            }
                        });
                        
                        _prefetchTasks[cacheKey] = prefetchTask;
                    }
                }
                
                _logger.LogInformation($"🚀 PREFETCH INITIATED: {_prefetchTasks.Count} background tasks started");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Prefetch trigger failed");
            }
            finally
            {
                _prefetchLock.Release();
            }
        }

        // REMOVED: Old loading status endpoints - no longer needed with lazy loading

        [HttpGet("test")]
        public IActionResult Test()
        {
            _logger.LogInformation("🧪 Test endpoint called successfully");
            return Ok(new { 
                message = "API is working", 
                timestamp = DateTime.UtcNow,
                version = "OPTIMIZED_LAZY_LOADING",
                features = new {
                    lazyLoading = true,
                    parallelLoading = true,
                    prefetching = true,
                    timeoutProtection = true,
                    memoryOptimization = true
                },
                memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024)
            });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { 
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "OPTIMIZED_LAZY_LOADING",
                memoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024),
                uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                mongoConnection = _collection != null ? "connected" : "disconnected"
            });
        }

        // REMOVED: Old trigger-reload endpoint - no longer needed with lazy loading

        [HttpPost("spin")]
        public async Task<IActionResult> Spin([FromBody] SpinRequestDto request)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                MemoryMonitor.TakeSnapshot("Spin_Start");
                var initialMemory = GC.GetTotalMemory(false);
                
                _logger.LogInformation($"🎰 SPIN REQUEST: Bet={request.Level}, Current RTP={SpinLogicHelper.GetActualRtp():P2}");
                
                // Validate bet parameters
                if (!BettingSystem.ValidateBet(request.Level, request.CoinValue, _config.MaxLevel, _config.MinCoinValue, _config.MaxCoinValue))
                {
                    return BadRequest(new { error = "Invalid bet parameters" });
                }

                // Calculate bet in coins and monetary value
                int betInCoins = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, request.Level);
                decimal totalBet = BettingSystem.CalculateTotalBet(_config.BaseBetPerLevel, request.Level, request.CoinValue);
                
                // 🚀 TIMEOUT PROTECTION: Add timeout to prevent hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10 second timeout
                
                // Get reel sets based on current RTP needs (lazy loading) with timeout
                var currentRtp = SpinLogicHelper.GetActualRtp();
                List<ReelSet> reelSets;
                
                try
                {
                    reelSets = await GetReelSetsForCurrentRtpAsync(currentRtp, _config.RtpTarget, _config).WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("❌ TIMEOUT: Reel set loading took too long (>10 seconds)");
                    return StatusCode(408, new { error = "Request timeout - reel set loading took too long. Please try again." });
                }
                
                MemoryMonitor.TakeSnapshot("After_ReelSet_Loading");
                var memoryAfterLoading = GC.GetTotalMemory(false);
                var memoryIncrease = memoryAfterLoading - initialMemory;
                
                _logger.LogInformation($"💾 MEMORY USAGE: +{memoryIncrease / (1024 * 1024):N0} MB for {reelSets.Count} reel sets");
                
                if (reelSets.Count == 0)
                {
                    return StatusCode(500, new { error = "No reel sets available for current RTP range." });
                }
                
                // Execute spin with loaded reel sets
                var (result, grid, chosenSet, winningLines) = SpinLogicHelper.SpinWithReelSets(_config, betInCoins, reelSets);
                
                if (result == null || grid == null || chosenSet == null)
                {
                    _logger.LogWarning("Spin returned null values - this might be due to no valid reel sets available");
                    return StatusCode(500, new { error = "Spin failed or was delayed." });
                }
                
                // Calculate monetary payout
                decimal monetaryPayout = BettingSystem.CalculatePayout((int)result.TotalWin, request.CoinValue);
                
                // Get actual RTP and Hit Rate from the helper
                var actualRtp = SpinLogicHelper.GetActualRtp();
                var actualHitRate = SpinLogicHelper.GetActualHitRate();
                
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var finalMemory = GC.GetTotalMemory(false);
                
                MemoryMonitor.TakeSnapshot("Spin_Complete");
                
                // Get memory pool stats
                var poolStats = ReelSetPool.GetStats();
                
                _logger.LogInformation($"✅ SPIN COMPLETED: {totalTime:F0}ms | Memory: {finalMemory / (1024 * 1024):N0} MB | Cache: {_rtpRangeCache.Count} ranges");
                _logger.LogInformation($"Spin completed successfully - TotalWin: {result.TotalWin} coins ({monetaryPayout:C}), RTP: {actualRtp}, HitRate: {actualHitRate}, WinningLines: {winningLines?.Count ?? 0}");
                _logger.LogInformation($"💾 MEMORY POOL: Created={poolStats.totalCreated}, Reused={poolStats.totalReused}, PoolSize={poolStats.poolSize}");
                
                return Ok(new
                {
                    grid,
                    result,
                    betInCoins,
                    totalBet,
                    monetaryPayout,
                    chosenReelSet = new {
                        chosenSet.Name,
                        chosenSet.ExpectedRtp,
                        chosenSet.EstimatedHitRate
                    },
                    rtp = actualRtp,
                    hitRate = actualHitRate,
                    winningLines = winningLines ?? new List<WinningLine>(),
                    performance = new
                    {
                        spinTimeMs = totalTime,
                        memoryUsageMB = finalMemory / (1024 * 1024),
                        cacheSize = _rtpRangeCache.Count,
                        reelSetsLoaded = reelSets.Count,
                        memoryIncreaseMB = memoryIncrease / (1024 * 1024),
                        poolStats = new
                        {
                            totalCreated = poolStats.totalCreated,
                            totalReused = poolStats.totalReused,
                            poolSize = poolStats.poolSize
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Spin failed");
                return StatusCode(500, new { error = "Spin failed", details = ex.Message });
            }
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            var memoryUsage = GC.GetTotalMemory(false);
            return Ok(new
            {
                memoryUsageMB = memoryUsage / (1024 * 1024),
                cacheSize = _rtpRangeCache.Count,
                totalReelSetsLoaded = _totalReelSetsLoaded,
                cacheRanges = _rtpRangeCache.Keys.ToList(),
                prefetchStats = new
                {
                    activePrefetchTasks = _prefetchTasks.Count,
                    lastPrefetchTime = _lastPrefetchTime,
                    prefetchInterval = _prefetchInterval.TotalSeconds
                },
                performance = new
                {
                    averageMemoryPerReelSet = _totalReelSetsLoaded > 0 ? memoryUsage / _totalReelSetsLoaded : 0,
                    cacheHitRate = "N/A" // Would need to track hits/misses
                }
            });
        }

        [HttpPost("clear-cache")]
        public IActionResult ClearCache()
        {
            var beforeMemory = GC.GetTotalMemory(false);
            var cacheSize = _rtpRangeCache.Count;
            
            _rtpRangeCache.Clear();
            _totalReelSetsLoaded = 0;
            
            var afterMemory = GC.GetTotalMemory(false);
            var memoryFreed = beforeMemory - afterMemory;
            
            _logger.LogInformation($"🗑️ CACHE CLEARED: Freed {memoryFreed / (1024 * 1024):N0} MB | Removed {cacheSize} cache entries");
            
            return Ok(new
            {
                message = "Cache cleared successfully",
                memoryFreedMB = memoryFreed / (1024 * 1024),
                cacheEntriesRemoved = cacheSize
            });
        }
    }

    public class SpinRequestDto
    {
        public int BetAmount { get; set; } = 25; // Legacy support
        public int Level { get; set; } = 1;
        public decimal CoinValue { get; set; } = 0.10m;
        // Add more fields as needed (e.g., user/session info)
    }
} 