using System.Collections.Concurrent;
using BloodSuckersSlot.Api.Controllers;

namespace BloodSuckersSlot.Api.Services
{
    public interface IPlayerSpinSessionService
    {
        SpinLogicHelper GetOrCreatePlayerSession(string playerId);
        void RemovePlayerSession(string playerId);
        SpinLogicHelper GetPlayerSession(string playerId);
        bool HasPlayerSession(string playerId);
        void ClearAllSessions();
        int GetActiveSessionCount();
        void CleanupInactiveSessions(TimeSpan inactivityThreshold);
    }

    public class PlayerSpinSessionService : IPlayerSpinSessionService
    {
        private readonly ConcurrentDictionary<string, SpinLogicHelper> _playerSessions = new();
        private readonly ConcurrentDictionary<string, DateTime> _sessionLastActivity = new();
        private readonly ILogger<PlayerSpinSessionService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggerFactory _loggerFactory;

        public PlayerSpinSessionService(ILogger<PlayerSpinSessionService> logger, IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _loggerFactory = loggerFactory;
        }

        public SpinLogicHelper GetOrCreatePlayerSession(string playerId)
        {
            var session = _playerSessions.GetOrAdd(playerId, id =>
            {
                _logger.LogInformation($"🎯 CREATED NEW PLAYER SESSION: {id}");
                // Create SpinLogicHelper directly without DI resolution for speed
                return new SpinLogicHelper(_loggerFactory.CreateLogger<SpinLogicHelper>());
            });
            
            // Update last activity time
            _sessionLastActivity.AddOrUpdate(playerId, DateTime.UtcNow, (key, oldValue) => DateTime.UtcNow);
            
            return session;
        }

        public void RemovePlayerSession(string playerId)
        {
            if (_playerSessions.TryRemove(playerId, out var session))
            {
                _sessionLastActivity.TryRemove(playerId, out _);
                _logger.LogInformation($"🗑️ REMOVED PLAYER SESSION: {playerId}");
            }
        }

        public SpinLogicHelper GetPlayerSession(string playerId)
        {
            _playerSessions.TryGetValue(playerId, out var session);
            return session;
        }

        public bool HasPlayerSession(string playerId)
        {
            return _playerSessions.ContainsKey(playerId);
        }

        public void ClearAllSessions()
        {
            var count = _playerSessions.Count;
            _playerSessions.Clear();
            _sessionLastActivity.Clear();
            _logger.LogInformation($"🗑️ CLEARED ALL PLAYER SESSIONS: {count} sessions removed");
        }

        public int GetActiveSessionCount()
        {
            return _playerSessions.Count;
        }

        public void CleanupInactiveSessions(TimeSpan inactivityThreshold)
        {
            var cutoffTime = DateTime.UtcNow - inactivityThreshold;
            var sessionsToRemove = new List<string>();

            foreach (var kvp in _sessionLastActivity)
            {
                if (kvp.Value < cutoffTime)
                {
                    sessionsToRemove.Add(kvp.Key);
                }
            }

            foreach (var playerId in sessionsToRemove)
            {
                RemovePlayerSession(playerId);
                _logger.LogInformation($"🧹 CLEANED UP INACTIVE SESSION: {playerId} (inactive for {DateTime.UtcNow - _sessionLastActivity.GetValueOrDefault(playerId, DateTime.UtcNow):hh\\:mm\\:ss})");
            }

            if (sessionsToRemove.Count > 0)
            {
                _logger.LogInformation($"🧹 CLEANUP COMPLETED: Removed {sessionsToRemove.Count} inactive SpinLogicHelper sessions");
            }
        }
    }
}
