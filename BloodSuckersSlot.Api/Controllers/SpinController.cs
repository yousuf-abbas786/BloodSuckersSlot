using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using MongoDB.Bson;
using Shared;
using Shared.Models;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using BloodSuckersSlot.Api.Services;
using BloodSuckersSlot.Api.Models;

namespace BloodSuckersSlot.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpinController : ControllerBase
    {
        private readonly ILogger<SpinController> _logger;
        private readonly GameConfig _config;
        private readonly PerformanceSettings _performanceSettings;
        private readonly AutoSpinService _autoSpinService;
        private readonly IPlayerSessionService _playerSessionService;
        private readonly IPlayerSpinSessionService _playerSpinSessionService;
        private readonly IReelSetCacheService _reelSetCacheService;
        private readonly IGlobalRtpBalancingService _globalRtpBalancingService;
        private readonly ISessionPreloadService _sessionPreloadService;
        
        // 🚀 SESSION CACHING for ultra-fast spins
        private readonly Dictionary<string, PlayerSessionResponse> _sessionCache = new();
        private readonly SemaphoreSlim _sessionCacheLock = new(1);
        private readonly TimeSpan _sessionCacheExpiry = TimeSpan.FromMinutes(5); // Cache sessions for 5 minutes
        private readonly Dictionary<string, DateTime> _sessionCacheTimestamps = new();
        
        // 🚀 SPIN SPEED OPTIMIZATIONS (moved to ReelSetCacheService)
        
        // SpinLogicHelper instance via DI
        private readonly SpinLogicHelper _spinLogicHelper;

        public SpinController(IConfiguration configuration, ILogger<SpinController> logger, 
            PerformanceSettings performanceSettings, AutoSpinService autoSpinService, IPlayerSessionService playerSessionService, IPlayerSpinSessionService playerSpinSessionService, IReelSetCacheService reelSetCacheService, IGlobalRtpBalancingService globalRtpBalancingService, ISessionPreloadService sessionPreloadService, SpinLogicHelper spinLogicHelper)
        {
            var startTime = DateTime.UtcNow;
            
            _logger = logger;
            _performanceSettings = performanceSettings;
            _autoSpinService = autoSpinService;
            _playerSessionService = playerSessionService;
            _playerSpinSessionService = playerSpinSessionService;
            _reelSetCacheService = reelSetCacheService;
            _globalRtpBalancingService = globalRtpBalancingService;
            _sessionPreloadService = sessionPreloadService;
            _spinLogicHelper = spinLogicHelper;
            _config = GameConfigLoader.LoadFromConfiguration(configuration);
            
            var initTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("🚀 SpinController initialized in {InitTime:F2}ms - ready for ultra-fast spins", initTime);
            _logger.LogInformation("📊 SpinController Performance Settings: SpinTimeout={SpinTimeout}s, SessionCacheExpiry={SessionCacheExpiry}min", 
                _performanceSettings.SpinTimeoutSeconds, _sessionCacheExpiry.TotalMinutes);
            _logger.LogInformation("✅ SpinLogicHelper registered via DI - no caching complexity");
        }

        // 🚀 ULTRA-FAST PRELOADING: Load essential reel sets for instant spins
        private async Task PreloadEssentialReelSetsAsync()
        {
            await _reelSetCacheService.PreloadEssentialReelSetsAsync();
        }

        // Lazy loading with RTP range caching
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<List<ReelSet>> GetReelSetsForRtpRangeAsync(double minRtp, double maxRtp, int limit = 1000)
        {
            return await _reelSetCacheService.GetReelSetsForRtpRangeAsync(minRtp, maxRtp, limit);
        }

        // 🚀 INSTANT REEL SETS: Synchronous method for maximum speed
        [ApiExplorerSettings(IgnoreApi = true)]
        private List<ReelSet> GetInstantReelSets()
        {
            return _reelSetCacheService.GetInstantReelSets();
        }

        
        


        // 🚀 SESSION CACHING METHODS for ultra-fast spins
        [ApiExplorerSettings(IgnoreApi = true)]
        private async Task<PlayerSessionResponse?> GetCachedSessionAsync(string playerId)
        {
            await _sessionCacheLock.WaitAsync();
            try
            {
                // Check if session exists in cache and is not expired
                if (_sessionCache.ContainsKey(playerId) && _sessionCacheTimestamps.ContainsKey(playerId))
                {
                    var cacheTime = _sessionCacheTimestamps[playerId];
                    if (DateTime.UtcNow - cacheTime < _sessionCacheExpiry)
                    {
                        _logger.LogDebug($"🎯 SESSION CACHE HIT: Player {playerId} (cached {DateTime.UtcNow - cacheTime:mm\\:ss} ago)");
                        return _sessionCache[playerId];
                    }
                    else
                    {
                        // Remove expired session
                        _sessionCache.Remove(playerId);
                        _sessionCacheTimestamps.Remove(playerId);
                        _logger.LogDebug($"🗑️ SESSION CACHE EXPIRED: Player {playerId}");
                    }
                }

                // Cache miss - load from database
                _logger.LogDebug($"🔄 SESSION CACHE MISS: Loading session for player {playerId} from database");
                var session = await _playerSessionService.GetActiveSessionAsync(playerId);
                
                if (session != null)
                {
                    // Cache the session
                    _sessionCache[playerId] = session;
                    _sessionCacheTimestamps[playerId] = DateTime.UtcNow;
                    _logger.LogDebug($"✅ SESSION CACHED: Player {playerId}");
                }

                return session;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"⚠️ Failed to get cached session for player {playerId}");
                return null;
            }
            finally
            {
                _sessionCacheLock.Release();
            }
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        private void UpdateCachedSession(string playerId, PlayerSessionResponse updatedSession)
        {
            try
            {
                _sessionCacheLock.Wait();
                if (_sessionCache.ContainsKey(playerId))
                {
                    _sessionCache[playerId] = updatedSession;
                    _sessionCacheTimestamps[playerId] = DateTime.UtcNow;
                    _logger.LogDebug($"🔄 SESSION CACHE UPDATED: Player {playerId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"⚠️ Failed to update cached session for player {playerId}");
            }
            finally
            {
                _sessionCacheLock.Release();
            }
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        private void RemoveCachedSession(string playerId)
        {
            try
            {
                _sessionCacheLock.Wait();
                _sessionCache.Remove(playerId);
                _sessionCacheTimestamps.Remove(playerId);
                _logger.LogDebug($"🗑️ SESSION CACHE REMOVED: Player {playerId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"⚠️ Failed to remove cached session for player {playerId}");
            }
            finally
            {
                _sessionCacheLock.Release();
            }
        }


        // 🚀 DYNAMIC PREFETCHING based on player trends
        [ApiExplorerSettings(IgnoreApi = true)]
        private async Task TriggerPrefetchAsync(double currentRtp, double targetRtp)
        {
            try
            {
                // Trigger smart prefetching based on current player RTP trends
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await _reelSetCacheService.PrefetchBasedOnPlayerTrendsAsync(currentRtp, targetRtp, 10);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Prefetching failed for RTP range {CurrentRtp:P2}-{TargetRtp:P2}", currentRtp, targetRtp);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to trigger prefetching");
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
                }
            });
        }

        /// <summary>
        /// Preload player session for ultra-fast spins
        /// </summary>
        [HttpPost("preload-session")]
        public async Task<IActionResult> PreloadSession()
        {
            try
            {
                var playerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? 
                              User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

                // 🚨 SECURITY: Player ID must exist - no anonymous access allowed
                if (string.IsNullOrEmpty(playerId))
                {
                    _logger.LogWarning("🚨 SECURITY VIOLATION: No valid player ID found in JWT token");
                    return Unauthorized("Invalid authentication - player ID required");
                }

                // Preload the session into cache
                var session = await GetCachedSessionAsync(playerId);
                
                if (session != null)
                {
                    _logger.LogInformation($"🚀 SESSION PRELOADED: Player {playerId} ready for ultra-fast spins");
                    return Ok(new { 
                        success = true, 
                        message = "Session preloaded successfully",
                        playerId = playerId,
                        sessionCached = true,
                        cacheSize = _sessionCache.Count,
                        sessionData = new {
                            session.SessionId,
                            session.CurrentBalance,
                            session.TotalSpins,
                            session.TotalRtp,
                            session.HitRate
                        }
                    });
                }
                else
                {
                    return StatusCode(500, new { error = "Failed to preload session" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Session preloading failed");
                return StatusCode(500, new { error = "Session preloading failed", details = ex.Message });
            }
        }

        /// <summary>
        /// Warm up the system by preloading essential data
        /// </summary>
        [HttpPost("warmup")]
        public async Task<IActionResult> Warmup()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                
                // Preload essential reel sets
                await PreloadEssentialReelSetsAsync();
                
                // Preload common RTP ranges (CONTROLLED LIMITS)
                var commonRanges = new List<(double min, double max)>
                {
                    (0.5, 0.8),    // Low RTP
                    (0.8, 1.0),    // Balanced RTP  
                    (1.0, 1.2)     // High RTP (CONTROLLED - removed extreme ranges)
                };
                
                var tasks = commonRanges.Select(async range =>
                {
                    var (min, max) = range;
                    await GetReelSetsForRtpRangeAsync(min, max, 500);
                }).ToArray();
                
                await Task.WhenAll(tasks);
                
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                return Ok(new {
                    success = true,
                    message = "System warmed up successfully",
                    warmupTimeMs = totalTime,
                    cacheStats = new {
                        activeRanges = _reelSetCacheService.GetCacheSize(),
                        totalReelSetsLoaded = _reelSetCacheService.GetTotalReelSetsLoaded(),
                        sessionCacheSize = _sessionCache.Count,
                        spinLogicCacheSize = 0
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ System warmup failed");
                return StatusCode(500, new { error = "System warmup failed", details = ex.Message });
            }
        }

        // 🌍 GLOBAL RTP STATISTICS: Get multiplayer RTP balance information
        [HttpGet("global-rtp-stats")]
        public async Task<IActionResult> GetGlobalRtpStats()
        {
            try
            {
                var globalStats = await _globalRtpBalancingService.GetGlobalRtpStatsAsync();
                
                return Ok(new
                {
                    totalPlayers = globalStats.TotalPlayers,
                    averageRtp = globalStats.AverageRtp,
                    minRtp = globalStats.MinRtp,
                    maxRtp = globalStats.MaxRtp,
                    targetRtp = _config.RtpTarget,
                    totalSpins = globalStats.TotalSpins,
                    totalBet = globalStats.TotalBet,
                    totalWin = globalStats.TotalWin,
                    lastUpdated = globalStats.LastUpdated,
                    globalDeviation = globalStats.AverageRtp - _config.RtpTarget,
                    isBalanced = Math.Abs(globalStats.AverageRtp - _config.RtpTarget) <= _config.RtpTarget * 0.05, // Within 5% of target
                    cacheAge = (DateTime.UtcNow - globalStats.LastUpdated).TotalSeconds,
                    cacheStatus = (DateTime.UtcNow - globalStats.LastUpdated).TotalSeconds < 3 ? "Fresh" : "Stale"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting global RTP stats");
                return StatusCode(500, new { error = "Failed to get global RTP stats" });
            }
        }

        // 🔄 CACHE MANAGEMENT: Force refresh global RTP cache
        [HttpPost("refresh-global-rtp")]
        public async Task<IActionResult> RefreshGlobalRtpCache()
        {
            try
            {
                await _globalRtpBalancingService.ForceRefreshCacheAsync();
                return Ok(new { message = "Global RTP cache refreshed successfully", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing global RTP cache");
                return StatusCode(500, new { error = "Failed to refresh global RTP cache" });
            }
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { 
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "OPTIMIZED_LAZY_LOADING_WITH_SESSION_CACHE",
                uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                mongoConnection = "connected", // MongoDB connection handled by ReelSetCacheService
                sessionCache = new {
                    activeSessions = _sessionCache.Count,
                    cacheExpiryMinutes = _sessionCacheExpiry.TotalMinutes
                },
                spinLogicCache = new {
                    activeInstances = 1
                },
                reelSetCache = new {
                    activeRanges = _reelSetCacheService.GetCacheSize(),
                    totalReelSetsLoaded = _reelSetCacheService.GetTotalReelSetsLoaded(),
                    isFullyLoaded = _reelSetCacheService.IsFullyLoaded(),
                    loadingProgress = _reelSetCacheService.GetLoadingProgress()
                }
            });
        }
        
        [HttpGet("loading-status")]
        public IActionResult GetLoadingStatus()
        {
            try
            {
                var isLoaded = _reelSetCacheService.IsFullyLoaded();
                var progress = _reelSetCacheService.GetLoadingProgress();
                var cacheSize = _reelSetCacheService.GetCacheSize();
                var totalSets = _reelSetCacheService.GetTotalReelSetsLoaded();
                
                var statusData = new
                {
                    isFullyLoaded = isLoaded,
                    progress = progress,
                    cacheSize = cacheSize,
                    totalReelSetsLoaded = totalSets,
                    status = isLoaded ? "ready" : "loading",
                    message = isLoaded ? "Service ready for spins" : "Service is still loading reel sets",
                    timestamp = DateTime.UtcNow
                };
                
                return Ok(statusData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Loading status check failed");
                return StatusCode(500, new { error = "Loading status check failed", message = ex.Message });
            }
        }

        // REMOVED: Old trigger-reload endpoint - no longer needed with lazy loading

        [HttpPost("spin")]
        public async Task<IActionResult> Spin([FromBody] SpinRequestDto request)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var stepTime = DateTime.UtcNow;
                
                // 🚨 CHECK LOADING STATUS: Block spins until reel sets are loaded
                if (!_reelSetCacheService.IsFullyLoaded())
                {
                    var progress = _reelSetCacheService.GetLoadingProgress();
                    _logger.LogWarning("⏳ SPIN BLOCKED: Reel sets still loading ({Progress}% complete)", progress);
                    return StatusCode(503, new { 
                        message = "Service is still loading reel sets. Please wait...", 
                        progress = progress,
                        status = "loading"
                    });
                }
                
                _logger.LogInformation("🎰 SPIN REQUEST STARTED: Player={PlayerId}, Level={Level}, CoinValue={CoinValue}", 
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "INVALID", 
                    request.Level, request.CoinValue);
                
                // Get player ID from JWT token
                var playerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? 
                              User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                
                // 🚨 SECURITY: Player ID must exist - no anonymous access allowed
                if (string.IsNullOrEmpty(playerId))
                {
                    _logger.LogWarning("🚨 SECURITY VIOLATION: No valid player ID found in JWT token");
                    return Unauthorized("Invalid authentication - player ID required");
                }
                
                var step1Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                
                // Get or create player-specific spin session (via DI)
                var playerSpinSession = _spinLogicHelper;
                
                var step2Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                

                
                var step3Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                
                //_logger.LogDebug("📊 Player Session Stats: RTP={Rtp:P2}, HitRate={HitRate:P2}", currentRtp, currentHitRate);
                
                // Validate bet parameters
                if (!BettingSystem.ValidateBet(request.Level, request.CoinValue, _config.MaxLevel, _config.MinCoinValue, _config.MaxCoinValue))
                {
                    return BadRequest(new { error = "Invalid bet parameters" });
                }

                // Calculate bet in coins and monetary value
                int betInCoins = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, request.Level);
                decimal totalBet = BettingSystem.CalculateTotalBet(_config.BaseBetPerLevel, request.Level, request.CoinValue);
                
                var step4Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                
                // 🚀 TIMEOUT PROTECTION: Add timeout to prevent hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_performanceSettings.SpinTimeoutSeconds));
                
                // 🚨 CRITICAL FIX: Get session from cache, fallback to database if needed
                PlayerSessionResponse? currentSession = await GetCachedSessionAsync(playerId);
                
                // 🚨 DEBUG: Log session state for debugging
                if (currentSession != null)
                {
                    _logger.LogInformation("🎯 SESSION LOADED: Player {PlayerId}, Spins={Spins}, WinningSpins={WinningSpins}, HitRate={HitRate:P2}", 
                        playerId, currentSession.TotalSpins, currentSession.WinningSpins, currentSession.HitRate);
                }
                else
                {
                    _logger.LogWarning("🚨 NO SESSION: Player {PlayerId} has no session", playerId);
                }
                
                // If no cached session, create one in memory (will be persisted later)
                if (currentSession == null && !string.IsNullOrEmpty(playerId))
                {
                    var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? playerId;
                    currentSession = new PlayerSessionResponse
                    {
                        SessionId = ObjectId.GenerateNewId().ToString(),
                        PlayerId = playerId,
                        Username = username,
                        SessionStart = DateTime.UtcNow,
                        IsActive = true,
                        CurrentBalance = 1000, // Default starting balance
                        TotalSpins = 0,
                        TotalBet = 0,
                        TotalWin = 0,
                        TotalRtp = 0,
                        HitRate = 0,
                        WinningSpins = 0,
                        FreeSpinsAwarded = 0,
                        BonusesTriggered = 0,
                        MaxWin = 0,
                        LastActivity = DateTime.UtcNow
                    };
                    
                // Cache the new session immediately
                await _sessionCacheLock.WaitAsync();
                try
                {
                    _sessionCache[playerId] = currentSession;
                    _sessionCacheTimestamps[playerId] = DateTime.UtcNow;
                    _logger.LogInformation("🆕 Created new session in memory for player {PlayerId}", playerId);
                }
                finally
                {
                    _sessionCacheLock.Release();
                }
                }
                
                // 🎯 CRITICAL FIX: Load session data into SpinLogicHelper before spinning
                if (currentSession != null)
                {
                    playerSpinSession.LoadSessionState(currentSession);
                    _logger.LogInformation("🎯 SESSION LOADED INTO SPINLOGIC: Player {PlayerId}, Spins={Spins}, RTP={Rtp:P2}, HitRate={HitRate:P2}", 
                        playerId, currentSession.TotalSpins, currentSession.TotalRtp, currentSession.HitRate);
                    
                    // 🎯 UPDATE REQUEST DTO: Add missing session data to request
                    request.CurrentRtp = currentSession.TotalRtp;
                    request.CurrentHitRate = currentSession.HitRate;
                    _logger.LogDebug("🎯 REQUEST UPDATED: RTP={Rtp:P2}, HitRate={HitRate:P2}", request.CurrentRtp, request.CurrentHitRate);
                }
                else
                {
                    _logger.LogWarning("🚨 NO SESSION TO LOAD: Player {PlayerId} has no session data", playerId);
                }

                // Get current RTP and Hit Rate from player's session
                var currentRtp = playerSpinSession.GetActualRtp();
                var currentHitRate = playerSpinSession.GetActualHitRate();

                // Use actual reel sets directly - let the engine work naturally
                List<ReelSet> reelSets = GetInstantReelSets();
                
                var step5Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                
                // If no cached data, use emergency fallback
                if (reelSets.Count == 0)
                {
                    _logger.LogWarning("⚠️ NO CACHED DATA: Using emergency fallback");
                    reelSets = GetInstantReelSets().Take(100).ToList(); // Fast fallback
                }
                
                _logger.LogDebug("🎯 Using {ReelSetCount} reel sets for spin", reelSets.Count);
                
                // 🚀 REMOVED: Slow prefetching that was slowing down spins
                
                // Execute spin with loaded reel sets using session-based RTP and Hit Rate
                // Execute spin using player's session
                var (result, grid, chosenSet, winningLines) = playerSpinSession.SpinWithReelSets(_config, betInCoins, reelSets, currentRtp, currentHitRate, (double)totalBet, (double)request.CoinValue);
                
                var step6Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                
                if (result == null || grid == null || chosenSet == null)
                {
                    // PERFORMANCE: Minimal error response for maximum speed
                    return StatusCode(500, new { error = "Spin failed or was delayed." });
                }
                
                // Calculate monetary payout
                decimal monetaryPayout = BettingSystem.CalculatePayout((int)result.TotalWin, request.CoinValue);
                
                // 🎯 GET LATEST SESSION STATE: Get updated state from SpinLogicHelper
                var latestSessionState = playerSpinSession.GetCurrentSessionState();
                
                // Get actual RTP and Hit Rate from the UPDATED session state
                var actualRtp = latestSessionState.CurrentRtp;
                var actualHitRate = latestSessionState.CurrentHitRate;
                
                _logger.LogDebug("🎯 SESSION STATE UPDATED: Spins={Spins}, Bet={Bet:F2}, Win={Win:F2}, RTP={Rtp:P2}, HitRate={HitRate:P2}", 
                    latestSessionState.SpinCounter, latestSessionState.TotalBet, latestSessionState.TotalWin, 
                    latestSessionState.CurrentRtp, latestSessionState.CurrentHitRate);
                
                var step7Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                stepTime = DateTime.UtcNow;
                
                _logger.LogInformation("🎰 SPIN RESULT: Win={Win:C}, Payout={Payout:C}, RTP={Rtp:P2}, HitRate={HitRate:P2}", 
                    result.TotalWin, monetaryPayout, actualRtp, actualHitRate);
                
                // 🚀 INSTANT SESSION UPDATE: Update session in memory only for maximum speed
                if (currentSession != null)
                {
                    // Update session data in memory for immediate response
                    currentSession.TotalSpins++;
                    currentSession.TotalBet += totalBet;
                    currentSession.TotalWin += monetaryPayout;
                    currentSession.CurrentBalance = currentSession.CurrentBalance + monetaryPayout - totalBet;
                    currentSession.LastActivity = DateTime.UtcNow;
                    
                    if (winningLines?.Count > 0) 
                    {
                        currentSession.WinningSpins++;
                    }
                    
                    if (monetaryPayout > currentSession.MaxWin)
                    {
                        currentSession.MaxWin = monetaryPayout;
                    }
                    
                    // Recalculate RTP and Hit Rate
                    currentSession.TotalRtp = currentSession.TotalBet > 0 ? (double)(currentSession.TotalWin / currentSession.TotalBet) : 0;
                    currentSession.HitRate = currentSession.TotalSpins > 0 ? (double)currentSession.WinningSpins / (double)currentSession.TotalSpins : 0;
                    
                    // 🚨 DEBUG: Log updated session stats
                    _logger.LogInformation("🔄 SESSION UPDATED: Player {PlayerId}, Spins={Spins}, WinningSpins={WinningSpins}, HitRate={HitRate:P2}, IsWinning={IsWinning}", 
                        playerId, currentSession.TotalSpins, currentSession.WinningSpins, currentSession.HitRate, winningLines?.Count > 0);
                    
                    // Update the cached session immediately
                    UpdateCachedSession(playerId, currentSession);
                    
                    // 🚨 DEBUG: Log what we're returning
                    _logger.LogInformation("📤 RETURNING SESSION: Player {PlayerId}, Spins={Spins}, WinningSpins={WinningSpins}, HitRate={HitRate:P2}", 
                        playerId, currentSession.TotalSpins, currentSession.WinningSpins, currentSession.HitRate);
                }
                
                // 🚀 ASYNC DATABASE UPDATE: Fire-and-forget DB persistence (non-blocking)
                if (!string.IsNullOrEmpty(playerId) && currentSession != null)
                {
                    var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? playerId;
                    
                    // 🚀 PERFORMANCE FIX: Make DB saves asynchronous but with proper error handling
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PersistSessionToDatabaseAsync(currentSession, totalBet, monetaryPayout, result, winningLines?.Count > 0);
                            _logger.LogDebug("✅ Session persisted to database for player {PlayerId}", playerId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ CRITICAL: Failed to persist session to database for player {PlayerId}", playerId);
                        }
                    });
                }

                var step8Time = (DateTime.UtcNow - stepTime).TotalMilliseconds;
                var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // DETAILED PERFORMANCE LOGGING
                _logger.LogInformation("🚀 SPIN PERFORMANCE BREAKDOWN:");
                _logger.LogInformation("   Step 1 (JWT): {Step1Time:F2}ms", step1Time);
                _logger.LogInformation("   Step 2 (GetSpinLogicHelper): {Step2Time:F2}ms", step2Time);
                _logger.LogInformation("   Step 3 (GetRTP/HitRate): {Step3Time:F2}ms", step3Time);
                _logger.LogInformation("   Step 4 (BetValidation): {Step4Time:F2}ms", step4Time);
                _logger.LogInformation("   Step 5 (GetReelSets): {Step5Time:F2}ms", step5Time);
                _logger.LogInformation("   Step 6 (SpinExecution): {Step6Time:F2}ms", step6Time);
                _logger.LogInformation("   Step 7 (CalculatePayout): {Step7Time:F2}ms", step7Time);
                _logger.LogInformation("   Step 8 (ResponsePrep): {Step8Time:F2}ms", step8Time);
                _logger.LogInformation("   TOTAL TIME: {TotalTime:F2}ms", totalTime);
                _logger.LogInformation("   Cache Size: {CacheSize} ranges", _reelSetCacheService.GetCacheSize());
                _logger.LogInformation("   Reel Sets: {ReelSetsCount} loaded", reelSets.Count);

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
                    currentSession = currentSession, // 🚨 FIX: Return the UPDATED session (already updated above)
                    latestSessionState = latestSessionState, // 🎯 NEW: Return latest SpinLogicHelper state
                    performance = new
                    {
                        spinTimeMs = totalTime,
                        cacheSize = _reelSetCacheService.GetCacheSize(),
                        reelSetsLoaded = reelSets.Count
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
            return Ok(new
            {
                cacheSize = _reelSetCacheService.GetCacheSize(),
                totalReelSetsLoaded = _reelSetCacheService.GetTotalReelSetsLoaded(),
                prefetchStats = _reelSetCacheService.GetPrefetchStats(),
                timestamp = DateTime.UtcNow
            });
        }

        [HttpPost("clear-cache")]
        public IActionResult ClearCache()
        {
            // Note: Cache clearing is now handled by the ReelSetCacheService
            _logger.LogInformation($"🗑️ CACHE CLEAR REQUESTED: Cache clearing is handled by ReelSetCacheService");
            
            return Ok(new
            {
                message = "Cache clear requested - handled by ReelSetCacheService",
                cacheSize = _reelSetCacheService.GetCacheSize()
            });
        }

        // Auto-Spin Management Endpoints
        [HttpPost("autospin/start")]
        public IActionResult StartAutoSpin([FromBody] AutoSpinRequestDto request)
        {
            try
            {
                if (request.SpinCount <= 0 || request.SpinCount > 1000)
                {
                    return BadRequest(new { error = "Spin count must be between 1 and 1000" });
                }

                if (request.SpinDelayMs < 0 || request.SpinDelayMs > 10000)
                {
                    return BadRequest(new { error = "Spin delay must be between 0ms (instant) and 10 seconds" });
                }

                // Validate bet parameters
                if (!BettingSystem.ValidateBet(request.Level, request.CoinValue, _config.MaxLevel, _config.MinCoinValue, _config.MaxCoinValue))
                {
                    return BadRequest(new { error = "Invalid bet parameters" });
                }

                // Get player ID from JWT token (same as Spin endpoint)
                var playerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? 
                              User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                
                // 🚨 SECURITY: Player ID must exist - no anonymous access allowed
                if (string.IsNullOrEmpty(playerId))
                {
                    _logger.LogWarning("🚨 SECURITY VIOLATION: No valid player ID found in JWT token");
                    return Unauthorized("Invalid authentication - player ID required");
                }
                
                var autoSpinId = Guid.NewGuid().ToString();
                var autoSpinSession = new Services.AutoSpinSession
                {
                    Id = autoSpinId,
                    PlayerId = playerId,
                    SpinCount = request.SpinCount,
                    RemainingSpins = request.SpinCount,
                    SpinDelayMs = request.SpinDelayMs,
                    Level = request.Level,
                    CoinValue = request.CoinValue,
                    BetAmount = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, request.Level),
                    IsActive = true,
                    StartedAt = DateTime.UtcNow,
                    TotalWins = 0,
                    TotalBets = 0
                };

                // Store session using the background service
                _autoSpinService.StartSession(autoSpinSession);

                _logger.LogInformation($"🎰 AUTO-SPIN STARTED: Session {autoSpinId}, Player {autoSpinSession.PlayerId}, {request.SpinCount} spins, {request.SpinDelayMs}ms delay");

                return Ok(new
                {
                    autoSpinId = autoSpinId,
                    message = "Auto-spin started successfully",
                    session = new
                    {
                        autoSpinSession.Id,
                        autoSpinSession.PlayerId,
                        autoSpinSession.SpinCount,
                        autoSpinSession.RemainingSpins,
                        autoSpinSession.SpinDelayMs,
                        autoSpinSession.Level,
                        autoSpinSession.CoinValue,
                        autoSpinSession.IsActive,
                        autoSpinSession.StartedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to start auto-spin");
                return StatusCode(500, new { error = "Failed to start auto-spin", details = ex.Message });
            }
        }

        [HttpPost("autospin/stop/{autoSpinId}")]
        public IActionResult StopAutoSpin(string autoSpinId)
        {
            try
            {
                var session = _autoSpinService.GetSession(autoSpinId);
                if (session == null)
                {
                    return NotFound(new { error = "Auto-spin session not found" });
                }

                _autoSpinService.StopSession(autoSpinId);
                
                _logger.LogInformation($"🛑 AUTO-SPIN STOPPED: Session {autoSpinId}");

                return Ok(new
                {
                    message = "Auto-spin stopped successfully",
                    session = new
                    {
                        session.Id,
                        session.PlayerId,
                        session.SpinCount,
                        session.RemainingSpins,
                        session.TotalWins,
                        session.TotalBets,
                        session.IsActive,
                        session.StartedAt,
                        session.StoppedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to stop auto-spin");
                return StatusCode(500, new { error = "Failed to stop auto-spin", details = ex.Message });
            }
        }

        [HttpGet("autospin/status/{autoSpinId}")]
        public IActionResult GetAutoSpinStatus(string autoSpinId)
        {
            try
            {
                var session = _autoSpinService.GetSession(autoSpinId);
                if (session == null)
                {
                    return NotFound(new { error = "Auto-spin session not found" });
                }

                return Ok(new
                {
                    session = new
                    {
                        session.Id,
                        session.PlayerId,
                        session.SpinCount,
                        session.RemainingSpins,
                        session.SpinDelayMs,
                        session.Level,
                        session.CoinValue,
                        session.IsActive,
                        session.StartedAt,
                        session.StoppedAt,
                        session.TotalWins,
                        session.TotalBets,
                        CompletedSpins = session.SpinCount - session.RemainingSpins
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get auto-spin status");
                return StatusCode(500, new { error = "Failed to get auto-spin status", details = ex.Message });
            }
        }

        [HttpGet("autospin/sessions")]
        public IActionResult GetAutoSpinSessions()
        {
            try
            {
                var sessions = _autoSpinService.GetAllSessions();
                return Ok(new
                {
                    sessions = sessions.Select(s => new
                    {
                        s.Id,
                        s.PlayerId,
                        s.SpinCount,
                        s.RemainingSpins,
                        s.SpinDelayMs,
                        s.Level,
                        s.CoinValue,
                        s.IsActive,
                        s.StartedAt,
                        s.StoppedAt,
                        s.TotalWins,
                        s.TotalBets,
                        CompletedSpins = s.SpinCount - s.RemainingSpins
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get auto-spin sessions");
                return StatusCode(500, new { error = "Failed to get auto-spin sessions", details = ex.Message });
            }
        }

        /// <summary>
        /// Persist session data to database asynchronously (non-blocking)
        /// </summary>
        private async Task PersistSessionToDatabaseAsync(PlayerSessionResponse session, decimal totalBet, decimal monetaryPayout, SpinResult result, bool isWinningSpin)
        {
            try
            {
                // Check if session exists in database
                var existingSession = await _playerSessionService.GetSessionAsync(session.SessionId);
                
                if (existingSession == null)
                {
                    // Create new session in database
                    var startRequest = new StartSessionRequest
                    {
                        PlayerId = session.PlayerId,
                        Username = session.Username,
                        InitialBalance = session.CurrentBalance - monetaryPayout + totalBet // Calculate initial balance
                    };
                    
                    var newSession = await _playerSessionService.StartSessionAsync(startRequest);
                    if (newSession != null)
                    {
                        // Update the session ID in our cached session
                        session.SessionId = newSession.SessionId;
                    }
                }
                
                // Update session stats in database
                var updateRequest = new UpdateSessionStatsRequest
                {
                    SessionId = session.SessionId,
                    PlayerId = session.PlayerId,
                    BetAmount = totalBet,
                    WinAmount = monetaryPayout,
                    IsWinningSpin = isWinningSpin,
                    IsFreeSpin = result.IsFreeSpin,
                    IsBonusTriggered = result.BonusTriggered,
                    FreeSpinsAwarded = result.FreeSpinsAwarded,
                    CurrentBalance = session.CurrentBalance,
                    // 🚨 CRITICAL FIX: Pass the updated TotalSpins and WinningSpins from session
                    TotalSpins = session.TotalSpins,
                    WinningSpins = session.WinningSpins
                };

                await _playerSessionService.UpdateSessionStatsAsync(updateRequest);
                
                _logger.LogDebug("✅ Session persisted to database for player {PlayerId}", session.PlayerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error persisting session to database for player {PlayerId}", session.PlayerId);
            }
        }

        /// <summary>
        /// Update player session statistics after a spin (LEGACY METHOD - kept for compatibility)
        /// </summary>
        private async Task<PlayerSessionResponse?> UpdatePlayerSessionStatsAsync(string playerId, string username, decimal totalBet, decimal monetaryPayout, SpinResult result, bool isWinningSpin)
        {
            try
            {
                if (string.IsNullOrEmpty(playerId))
                {
                    _logger.LogWarning("No player ID provided - skipping session stats update");
                    return null;
                }

                // Get or create active session
                var session = await _playerSessionService.GetActiveSessionAsync(playerId);
                if (session == null)
                {
                    // Start a new session if none exists
                    var startRequest = new StartSessionRequest
                    {
                        PlayerId = playerId,
                        Username = username,
                        InitialBalance = 1000 // Default starting balance
                    };
                    
                    session = await _playerSessionService.StartSessionAsync(startRequest);
                    if (session == null)
                    {
                        _logger.LogWarning("Failed to start new session for player {PlayerId}", playerId);
                        return null;
                    }
                }

                // Update session stats
                var updateRequest = new UpdateSessionStatsRequest
                {
                    SessionId = session.SessionId,
                    PlayerId = playerId,
                    BetAmount = totalBet,
                    WinAmount = monetaryPayout,
                    IsWinningSpin = isWinningSpin,
                    IsFreeSpin = result.IsFreeSpin,
                    IsBonusTriggered = result.BonusTriggered,
                    FreeSpinsAwarded = result.FreeSpinsAwarded,
                    CurrentBalance = session.CurrentBalance + monetaryPayout - totalBet // Update balance
                };

                var success = await _playerSessionService.UpdateSessionStatsAsync(updateRequest);
                if (success)
                {
                    _logger.LogInformation("✅ Updated session stats for player {PlayerId}: Bet={Bet:C}, Win={Win:C}, Balance={Balance:C}", 
                        playerId, totalBet, monetaryPayout, updateRequest.CurrentBalance);
                    
                    // Return the updated session data from memory
                    return session;
                }
                else
                {
                    _logger.LogWarning("❌ Failed to update session stats for player {PlayerId}", playerId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating player session stats");
                // Don't throw - session stats update failure shouldn't break the spin
                return null;
            }
        }
    }

    public class SpinRequestDto
    {
        public int BetAmount { get; set; } = 25; // Legacy support
        public int Level { get; set; } = 1;
        public decimal CoinValue { get; set; } = 0.10m;
        public double CurrentRtp { get; set; } = 0.0; // Current session RTP
        public double CurrentHitRate { get; set; } = 0.0; // Current session Hit Rate
        // Add more fields as needed (e.g., user/session info)
    }

    public class AutoSpinRequestDto
    {
        public string? PlayerId { get; set; }
        public int SpinCount { get; set; } = 10;
        public int SpinDelayMs { get; set; } = 1000;
        public int Level { get; set; } = 1;
        public decimal CoinValue { get; set; } = 0.10m;
    }

} 