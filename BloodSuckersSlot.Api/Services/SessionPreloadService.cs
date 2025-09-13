using Shared.Models;

namespace BloodSuckersSlot.Api.Services
{
    /// <summary>
    /// Service for preloading player sessions to improve spin performance
    /// </summary>
    public class SessionPreloadService : ISessionPreloadService
    {
        private readonly IPlayerSessionService _playerSessionService;
        private readonly ILogger<SessionPreloadService> _logger;
        
        // 🚀 SESSION CACHING for ultra-fast spins
        private readonly Dictionary<string, PlayerSessionResponse> _sessionCache = new();
        private readonly SemaphoreSlim _sessionCacheLock = new(1);
        private readonly TimeSpan _sessionCacheExpiry = TimeSpan.FromMinutes(5); // Cache sessions for 5 minutes
        private readonly Dictionary<string, DateTime> _sessionCacheTimestamps = new();

        public SessionPreloadService(IPlayerSessionService playerSessionService, ILogger<SessionPreloadService> logger)
        {
            _playerSessionService = playerSessionService;
            _logger = logger;
        }

        /// <summary>
        /// Preload player session into cache for faster access
        /// </summary>
        public async Task<bool> PreloadSessionAsync(string playerId)
        {
            try
            {
                await _sessionCacheLock.WaitAsync();
                
                // Check if already cached and not expired
                if (_sessionCache.ContainsKey(playerId) && _sessionCacheTimestamps.ContainsKey(playerId))
                {
                    var cacheTime = _sessionCacheTimestamps[playerId];
                    if (DateTime.UtcNow - cacheTime < _sessionCacheExpiry)
                    {
                        _logger.LogDebug($"🎯 SESSION ALREADY CACHED: Player {playerId} (cached {DateTime.UtcNow - cacheTime:mm\\:ss} ago)");
                        return true;
                    }
                }

                // Load session from database
                var session = await _playerSessionService.GetActiveSessionAsync(playerId);
                if (session == null)
                {
                    // Create new session if none exists using StartSessionAsync
                    var startRequest = new StartSessionRequest
                    {
                        PlayerId = playerId,
                        Username = "Unknown", // Will be updated when we have more context
                        InitialBalance = 1000 // Default balance
                    };
                    session = await _playerSessionService.StartSessionAsync(startRequest);
                }

                if (session != null)
                {
                    // Cache the session
                    _sessionCache[playerId] = session;
                    _sessionCacheTimestamps[playerId] = DateTime.UtcNow;
                    _logger.LogInformation($"🚀 SESSION PRELOADED: Player {playerId} session cached for fast spins");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to preload session for player {PlayerId}", playerId);
                return false;
            }
            finally
            {
                _sessionCacheLock.Release();
            }
        }

        /// <summary>
        /// Get cached session if available
        /// </summary>
        public async Task<PlayerSessionResponse?> GetCachedSessionAsync(string playerId)
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

                // Cache miss - return null (caller should load from database)
                return null;
            }
            finally
            {
                _sessionCacheLock.Release();
            }
        }

        /// <summary>
        /// Update cached session
        /// </summary>
        public void UpdateCachedSession(string playerId, PlayerSessionResponse session)
        {
            try
            {
                _sessionCacheLock.Wait();
                _sessionCache[playerId] = session;
                _sessionCacheTimestamps[playerId] = DateTime.UtcNow;
                _logger.LogDebug($"🔄 SESSION CACHE UPDATED: Player {playerId}");
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

        /// <summary>
        /// Remove session from cache
        /// </summary>
        public void RemoveCachedSession(string playerId)
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
    }
}
