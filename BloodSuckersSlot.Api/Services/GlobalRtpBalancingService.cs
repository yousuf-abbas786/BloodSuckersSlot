using MongoDB.Driver;
using MongoDB.Bson;
using BloodSuckersSlot.Api.Models;
using Shared.Models;
using Shared;
using Microsoft.Extensions.Configuration;

namespace BloodSuckersSlot.Api.Services
{
    public interface IGlobalRtpBalancingService
    {
        Task<double> GetGlobalAverageRtpAsync();
        Task<GlobalRtpStats> GetGlobalRtpStatsAsync();
        Task<bool> ShouldBoostPlayerRtpAsync(string playerId);
        Task<bool> ShouldReducePlayerRtpAsync(string playerId);
        Task<double> GetPlayerRtpAdjustmentFactorAsync(string playerId);
        Task UpdateGlobalRtpStatsAsync();
        Task ForceRefreshCacheAsync(); // Force immediate cache refresh
    }

    public class GlobalRtpBalancingService : IGlobalRtpBalancingService
    {
        private readonly IMongoCollection<PlayerSession> _sessionCollection;
        private readonly ILogger<GlobalRtpBalancingService> _logger;
        private readonly GameConfig _config;
        
        // Cache for performance with smart invalidation
        private GlobalRtpStats _cachedStats;
        private DateTime _lastStatsUpdate = DateTime.MinValue;
        private readonly TimeSpan _statsCacheTimeout = TimeSpan.FromSeconds(3); // Reduced to 3 seconds for better responsiveness
        
        // Smart invalidation tracking
        private double _lastKnownGlobalRtp = 0;
        private readonly double _significantChangeThreshold = 0.05; // 5% change triggers refresh
        private int _consecutiveStaleRequests = 0;
        private readonly int _maxStaleRequests = 3; // Force refresh after 3 stale requests

        public GlobalRtpBalancingService(IMongoDatabase database, ILogger<GlobalRtpBalancingService> logger, IConfiguration configuration)
        {
            _sessionCollection = database.GetCollection<PlayerSession>("playerSessions");
            _logger = logger;
            _config = GameConfigLoader.LoadFromConfiguration(configuration);
        }

        public async Task<double> GetGlobalAverageRtpAsync()
        {
            var stats = await GetGlobalRtpStatsAsync();
            return stats.AverageRtp;
        }

        public async Task<GlobalRtpStats> GetGlobalRtpStatsAsync()
        {
            // 🚀 HYBRID CACHING: Smart cache validation with fallback
            var now = DateTime.UtcNow;
            var cacheAge = now - _lastStatsUpdate;
            
            // Use cached stats if still valid and not too stale
            if (_cachedStats != null && cacheAge < _statsCacheTimeout)
            {
                _consecutiveStaleRequests = 0; // Reset stale counter
                return _cachedStats;
            }
            
            // 🔄 SMART INVALIDATION: Check if we need to refresh
            bool shouldRefresh = ShouldRefreshCache(cacheAge);
            
            if (!shouldRefresh && _cachedStats != null)
            {
                _consecutiveStaleRequests++;
                _logger.LogDebug("📊 Using stale cache (age: {CacheAge:F1}s, stale requests: {StaleCount})", 
                    cacheAge.TotalSeconds, _consecutiveStaleRequests);
                return _cachedStats;
            }

            try
            {
                // Get all active sessions
                var activeSessions = await _sessionCollection
                    .Find(s => s.IsActive && s.TotalSpins > 0)
                    .ToListAsync();

                if (!activeSessions.Any())
                {
                    _cachedStats = new GlobalRtpStats
                    {
                        TotalPlayers = 0,
                        AverageRtp = _config.RtpTarget, // Default to target if no players
                        MinRtp = _config.RtpTarget,
                        MaxRtp = _config.RtpTarget,
                        TotalSpins = 0,
                        TotalBet = 0,
                        TotalWin = 0,
                        LastUpdated = DateTime.UtcNow
                    };
                    return _cachedStats;
                }

                // Calculate global statistics
                var totalSpins = activeSessions.Sum(s => s.TotalSpins);
                var totalBet = activeSessions.Sum(s => (double)s.TotalBet);
                var totalWin = activeSessions.Sum(s => (double)s.TotalWin);
                
                var averageRtp = totalBet > 0 ? totalWin / totalBet : _config.RtpTarget;
                var minRtp = activeSessions.Min(s => s.TotalRtp);
                var maxRtp = activeSessions.Max(s => s.TotalRtp);

                _cachedStats = new GlobalRtpStats
                {
                    TotalPlayers = activeSessions.Count,
                    AverageRtp = averageRtp,
                    MinRtp = minRtp,
                    MaxRtp = maxRtp,
                    TotalSpins = totalSpins,
                    TotalBet = totalBet,
                    TotalWin = totalWin,
                    LastUpdated = DateTime.UtcNow
                };

                _lastStatsUpdate = DateTime.UtcNow;
                _consecutiveStaleRequests = 0; // Reset stale counter

                // 🔄 SMART INVALIDATION: Track significant changes
                if (_lastKnownGlobalRtp > 0)
                {
                    var rtpChange = Math.Abs(_cachedStats.AverageRtp - _lastKnownGlobalRtp);
                    if (rtpChange > _significantChangeThreshold)
                    {
                        _logger.LogInformation("📈 SIGNIFICANT RTP CHANGE: {OldRtp:P2} → {NewRtp:P2} (Δ{Change:P2})", 
                            _lastKnownGlobalRtp, _cachedStats.AverageRtp, rtpChange);
                    }
                }
                _lastKnownGlobalRtp = _cachedStats.AverageRtp;

                _logger.LogInformation("🌍 GLOBAL RTP STATS: Players={Players}, AvgRTP={AvgRtp:P2}, MinRTP={MinRtp:P2}, MaxRTP={MaxRtp:P2}, Target={Target:P2}",
                    _cachedStats.TotalPlayers, _cachedStats.AverageRtp, _cachedStats.MinRtp, _cachedStats.MaxRtp, _config.RtpTarget);

                return _cachedStats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating global RTP stats");
                return new GlobalRtpStats
                {
                    TotalPlayers = 0,
                    AverageRtp = _config.RtpTarget,
                    MinRtp = _config.RtpTarget,
                    MaxRtp = _config.RtpTarget,
                    TotalSpins = 0,
                    TotalBet = 0,
                    TotalWin = 0,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        public async Task<bool> ShouldBoostPlayerRtpAsync(string playerId)
        {
            var globalStats = await GetGlobalRtpStatsAsync();
            var playerSession = await GetPlayerSessionAsync(playerId);

            if (playerSession == null || globalStats.TotalPlayers < 2)
                return false;

            // Boost player RTP if:
            // 1. Global average is below target
            // 2. Player's RTP is below global average
            // 3. Player has enough spins for meaningful data
            return globalStats.AverageRtp < _config.RtpTarget * 0.98 && // Global below target
                   playerSession.TotalRtp < globalStats.AverageRtp && // Player below average
                   playerSession.TotalSpins >= 10; // Enough spins for reliable data
        }

        public async Task<bool> ShouldReducePlayerRtpAsync(string playerId)
        {
            var globalStats = await GetGlobalRtpStatsAsync();
            var playerSession = await GetPlayerSessionAsync(playerId);

            if (playerSession == null || globalStats.TotalPlayers < 2)
                return false;

            // Reduce player RTP if:
            // 1. Global average is above target
            // 2. Player's RTP is above global average
            // 3. Player has enough spins for meaningful data
            return globalStats.AverageRtp > _config.RtpTarget * 1.02 && // Global above target
                   playerSession.TotalRtp > globalStats.AverageRtp && // Player above average
                   playerSession.TotalSpins >= 10; // Enough spins for reliable data
        }

        public async Task<double> GetPlayerRtpAdjustmentFactorAsync(string playerId)
        {
            var globalStats = await GetGlobalRtpStatsAsync();
            var playerSession = await GetPlayerSessionAsync(playerId);

            if (playerSession == null || globalStats.TotalPlayers < 2)
                return 1.0; // No adjustment

            // 🚀 AGGRESSIVE RTP CORRECTION: Multi-tier correction system
            double adjustmentFactor = CalculateAggressiveRtpAdjustment(playerSession, globalStats.AverageRtp);
            
            // 🔄 UPDATE TRACKING: Update acceleration tracking fields
            await UpdateRtpTrackingFieldsAsync(playerSession, globalStats.AverageRtp);

            _logger.LogDebug("🎯 AGGRESSIVE RTP ADJUSTMENT: Player={PlayerId}, GlobalAvg={GlobalAvg:P2}, PlayerRTP={PlayerRTP:P2}, Factor={Factor:F2}, SpinsAbove={SpinsAbove}",
                playerId, globalStats.AverageRtp, playerSession.TotalRtp, adjustmentFactor, playerSession.SpinsAboveTarget);

            return adjustmentFactor;
        }

        public async Task UpdateGlobalRtpStatsAsync()
        {
            // Force refresh of cached stats
            _lastStatsUpdate = DateTime.MinValue;
            await GetGlobalRtpStatsAsync();
        }

        public async Task ForceRefreshCacheAsync()
        {
            _logger.LogInformation("🔄 FORCE REFRESH: Immediately updating global RTP cache");
            _lastStatsUpdate = DateTime.MinValue;
            _consecutiveStaleRequests = 0;
            await GetGlobalRtpStatsAsync();
        }

        // 🔄 SMART INVALIDATION: Determine if cache should be refreshed
        private bool ShouldRefreshCache(TimeSpan cacheAge)
        {
            // Always refresh if cache is too old
            if (cacheAge > TimeSpan.FromSeconds(10))
            {
                _logger.LogDebug("🔄 Cache too old ({Age:F1}s) - forcing refresh", cacheAge.TotalSeconds);
                return true;
            }
            
            // Force refresh after too many stale requests
            if (_consecutiveStaleRequests >= _maxStaleRequests)
            {
                _logger.LogDebug("🔄 Too many stale requests ({Count}) - forcing refresh", _consecutiveStaleRequests);
                return true;
            }
            
            // Refresh if no cached data
            if (_cachedStats == null)
            {
                _logger.LogDebug("🔄 No cached data - forcing refresh");
                return true;
            }
            
            // Use stale cache for better performance
            return false;
        }

        // 🚀 IMPROVED RTP CORRECTION: Balanced multi-tier system with persistence handling
        private double CalculateAggressiveRtpAdjustment(PlayerSession playerSession, double globalAverageRtp)
        {
            var targetRtp = _config.RtpTarget;
            var playerRtp = playerSession.TotalRtp;
            var deviation = playerRtp - targetRtp;
            var deviationPercent = deviation / targetRtp;

            // 🚨 EXTREME PERSISTENCE: Handle players who stay above target for too long
            if (deviationPercent > 0.05 && playerSession.SpinsAboveTarget > 50) // 5%+ above target for 50+ spins
            {
                var persistencePenalty = Math.Min(0.9, (playerSession.SpinsAboveTarget - 50) * 0.02); // Up to 90% reduction
                var finalFactor = Math.Max(0.1, 1.0 - persistencePenalty);
                
                _logger.LogWarning("🚨 PERSISTENCE PENALTY: Player {PlayerId} {Deviation:P1} above target for {Spins} spins → Factor: {Factor:F3}",
                    playerSession.PlayerId, deviationPercent, playerSession.SpinsAboveTarget, finalFactor);
                
                return finalFactor;
            }

            // 🎯 TIER 1: ULTRA-AGGRESSIVE CORRECTION (>15% above target)
            if (deviationPercent > 0.15) // 15%+ above target
            {
                var ultraAggressiveReduction = Math.Min(0.85, deviationPercent * 4.0); // Up to 85% reduction
                var timeAcceleration = CalculateTimeAcceleration(playerSession);
                var spinAcceleration = CalculateSpinAcceleration(playerSession);
                
                var finalFactor = Math.Max(0.15, 1.0 - ultraAggressiveReduction * timeAcceleration * spinAcceleration);
                
                _logger.LogWarning("🚨 ULTRA-AGGRESSIVE CORRECTION: Player {PlayerId} {Deviation:P1} above target → Factor: {Factor:F3} (Time: {TimeAccel:F2}x, Spin: {SpinAccel:F2}x)",
                    playerSession.PlayerId, deviationPercent, finalFactor, timeAcceleration, spinAcceleration);
                
                return finalFactor;
            }
            
            // 🎯 TIER 2: AGGRESSIVE CORRECTION (10-15% above target)
            else if (deviationPercent > 0.10) // 10-15% above target
            {
                var aggressiveReduction = Math.Min(0.7, deviationPercent * 3.0); // Up to 70% reduction
                var timeAcceleration = CalculateTimeAcceleration(playerSession);
                var spinAcceleration = CalculateSpinAcceleration(playerSession);
                
                var finalFactor = Math.Max(0.3, 1.0 - aggressiveReduction * timeAcceleration * spinAcceleration);
                
                _logger.LogInformation("🚨 AGGRESSIVE CORRECTION: Player {PlayerId} {Deviation:P1} above target → Factor: {Factor:F3} (Time: {TimeAccel:F2}x, Spin: {SpinAccel:F2}x)",
                    playerSession.PlayerId, deviationPercent, finalFactor, timeAcceleration, spinAcceleration);
                
                return finalFactor;
            }
            
            // 🎯 TIER 3: MODERATE CORRECTION (5-10% above target)
            else if (deviationPercent > 0.05) // 5-10% above target
            {
                var moderateReduction = Math.Min(0.5, deviationPercent * 2.0); // Up to 50% reduction
                var timeAcceleration = CalculateTimeAcceleration(playerSession);
                
                var finalFactor = Math.Max(0.5, 1.0 - moderateReduction * timeAcceleration);
                
                _logger.LogDebug("⚡ MODERATE CORRECTION: Player {PlayerId} {Deviation:P1} above target → Factor: {Factor:F3} (Time: {TimeAccel:F2}x)",
                    playerSession.PlayerId, deviationPercent, finalFactor, timeAcceleration);
                
                return finalFactor;
            }
            
            // 🎯 TIER 4: GENTLE CORRECTION (2-5% above target)
            else if (deviationPercent > 0.02) // 2-5% above target
            {
                var gentleReduction = deviationPercent * 1.5; // 1.5:1 reduction
                var finalFactor = Math.Max(0.7, 1.0 - gentleReduction);
                
                _logger.LogDebug("🎯 GENTLE CORRECTION: Player {PlayerId} {Deviation:P1} above target → Factor: {Factor:F3}",
                    playerSession.PlayerId, deviationPercent, finalFactor);
                
                return finalFactor;
            }
            
            // 🎯 BOOST TIER 1: STRONG BOOST (10%+ below target)
            else if (deviationPercent < -0.10) // 10%+ below target
            {
                var strongBoostAmount = Math.Min(0.5, Math.Abs(deviationPercent) * 2.0); // Up to 50% boost
                var finalFactor = Math.Min(1.8, 1.0 + strongBoostAmount);
                
                _logger.LogInformation("📈 STRONG RTP BOOST: Player {PlayerId} {Deviation:P1} below target → Factor: {Factor:F3}",
                    playerSession.PlayerId, deviationPercent, finalFactor);
                
                return finalFactor;
            }
            
            // 🎯 BOOST TIER 2: MODERATE BOOST (5-10% below target)
            else if (deviationPercent < -0.05) // 5-10% below target
            {
                var moderateBoostAmount = Math.Min(0.3, Math.Abs(deviationPercent) * 1.5); // Up to 30% boost
                var finalFactor = Math.Min(1.5, 1.0 + moderateBoostAmount);
                
                _logger.LogDebug("📈 MODERATE RTP BOOST: Player {PlayerId} {Deviation:P1} below target → Factor: {Factor:F3}",
                    playerSession.PlayerId, deviationPercent, finalFactor);
                
                return finalFactor;
            }
            
            // 🎯 BOOST TIER 3: GENTLE BOOST (2-5% below target)
            else if (deviationPercent < -0.02) // 2-5% below target
            {
                var gentleBoostAmount = Math.Abs(deviationPercent) * 1.0; // 1:1 boost
                var finalFactor = Math.Min(1.2, 1.0 + gentleBoostAmount);
                
                _logger.LogDebug("📈 GENTLE RTP BOOST: Player {PlayerId} {Deviation:P1} below target → Factor: {Factor:F3}",
                    playerSession.PlayerId, deviationPercent, finalFactor);
                
                return finalFactor;
            }
            
            // 🎯 NEUTRAL: Close to target (±2%)
            return 1.0;
        }

        // ⏰ IMPROVED TIME-BASED ACCELERATION: More aggressive for persistent offenders
        private double CalculateTimeAcceleration(PlayerSession playerSession)
        {
            if (playerSession.FirstAboveTargetTime == null)
                return 1.0;
            
            var timeAboveTarget = DateTime.UtcNow - playerSession.FirstAboveTargetTime.Value;
            var minutesAboveTarget = timeAboveTarget.TotalMinutes;
            
            // More aggressive acceleration: 1x → 5x over 10 minutes
            return Math.Min(5.0, 1.0 + minutesAboveTarget * 0.4);
        }

        // 🔢 IMPROVED SPIN-BASED ACCELERATION: More aggressive for persistent offenders
        private double CalculateSpinAcceleration(PlayerSession playerSession)
        {
            var spinsAboveTarget = playerSession.SpinsAboveTarget;
            
            // More aggressive acceleration: 1x → 3x over 50 spins
            return Math.Min(3.0, 1.0 + spinsAboveTarget * 0.04);
        }

        // 🔄 UPDATE TRACKING: Update acceleration tracking fields
        private async Task UpdateRtpTrackingFieldsAsync(PlayerSession playerSession, double globalAverageRtp)
        {
            var targetRtp = _config.RtpTarget;
            var isAboveTarget = playerSession.TotalRtp > targetRtp;
            
            // Track when player first goes above target
            if (isAboveTarget && playerSession.FirstAboveTargetTime == null)
            {
                playerSession.FirstAboveTargetTime = DateTime.UtcNow;
                _logger.LogInformation("📊 TRACKING START: Player {PlayerId} first above target at {Rtp:P2}",
                    playerSession.PlayerId, playerSession.TotalRtp);
            }
            
            // Reset tracking if player goes below target
            if (!isAboveTarget && playerSession.FirstAboveTargetTime != null)
            {
                playerSession.FirstAboveTargetTime = null;
                playerSession.SpinsAboveTarget = 0;
                _logger.LogInformation("📊 TRACKING RESET: Player {PlayerId} back below target at {Rtp:P2}",
                    playerSession.PlayerId, playerSession.TotalRtp);
            }
            
            // Increment spin counter if above target
            if (isAboveTarget)
            {
                playerSession.SpinsAboveTarget++;
            }
            
            // Update last adjustment factor
            playerSession.LastRtpAdjustment = CalculateAggressiveRtpAdjustment(playerSession, globalAverageRtp);
            
            // Save updated session
            try
            {
                await _sessionCollection.ReplaceOneAsync(
                    s => s.Id == playerSession.Id,
                    playerSession);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update RTP tracking fields for player {PlayerId}", playerSession.PlayerId);
            }
        }

        private async Task<PlayerSession?> GetPlayerSessionAsync(string playerId)
        {
            try
            {
                return await _sessionCollection
                    .Find(s => s.PlayerId == playerId && s.IsActive)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting player session for {PlayerId}", playerId);
                return null;
            }
        }
    }

    public class GlobalRtpStats
    {
        public int TotalPlayers { get; set; }
        public double AverageRtp { get; set; }
        public double MinRtp { get; set; }
        public double MaxRtp { get; set; }
        public long TotalSpins { get; set; }
        public double TotalBet { get; set; }
        public double TotalWin { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
