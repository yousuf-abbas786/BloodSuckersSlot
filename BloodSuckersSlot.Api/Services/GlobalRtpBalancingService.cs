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
    }

    public class GlobalRtpBalancingService : IGlobalRtpBalancingService
    {
        private readonly IMongoCollection<PlayerSession> _sessionCollection;
        private readonly ILogger<GlobalRtpBalancingService> _logger;
        private readonly GameConfig _config;
        
        // Cache for performance
        private GlobalRtpStats _cachedStats;
        private DateTime _lastStatsUpdate = DateTime.MinValue;
        private readonly TimeSpan _statsCacheTimeout = TimeSpan.FromSeconds(30);

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
            // Use cached stats if still valid
            if (_cachedStats != null && DateTime.UtcNow - _lastStatsUpdate < _statsCacheTimeout)
            {
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

            var globalDeviation = globalStats.AverageRtp - _config.RtpTarget;
            var playerDeviation = playerSession.TotalRtp - globalStats.AverageRtp;

            // Calculate adjustment factor
            // If global is below target and player is below average -> boost more
            // If global is above target and player is above average -> reduce more
            double adjustmentFactor = 1.0;

            if (globalDeviation < -0.05) // Global 5% below target
            {
                if (playerDeviation < -0.1) // Player 10% below average
                {
                    adjustmentFactor = 1.3; // Strong boost
                }
                else if (playerDeviation < -0.05) // Player 5% below average
                {
                    adjustmentFactor = 1.15; // Moderate boost
                }
            }
            else if (globalDeviation > 0.05) // Global 5% above target
            {
                if (playerDeviation > 0.1) // Player 10% above average
                {
                    adjustmentFactor = 0.7; // Strong reduction
                }
                else if (playerDeviation > 0.05) // Player 5% above average
                {
                    adjustmentFactor = 0.85; // Moderate reduction
                }
            }

            _logger.LogDebug("🎯 RTP ADJUSTMENT: Player={PlayerId}, GlobalAvg={GlobalAvg:P2}, PlayerRTP={PlayerRTP:P2}, Factor={Factor:F2}",
                playerId, globalStats.AverageRtp, playerSession.TotalRtp, adjustmentFactor);

            return adjustmentFactor;
        }

        public async Task UpdateGlobalRtpStatsAsync()
        {
            // Force refresh of cached stats
            _lastStatsUpdate = DateTime.MinValue;
            await GetGlobalRtpStatsAsync();
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
