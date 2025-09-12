using MongoDB.Driver;
using Shared.Models;
using MongoDB.Bson;
using System.Linq.Expressions;

namespace BloodSuckersSlot.Api.Services
{
    public interface IPlayerSessionService
    {
        Task<PlayerSessionResponse?> StartSessionAsync(StartSessionRequest request);
        Task<PlayerSessionResponse?> GetActiveSessionAsync(string playerId);
        Task<PlayerSessionResponse?> GetSessionAsync(string sessionId);
        Task<bool> EndSessionAsync(string sessionId);
        Task<bool> UpdateSessionStatsAsync(UpdateSessionStatsRequest request);
        Task<PlayerStatsResponse?> GetPlayerStatsAsync(string playerId);
        Task<List<PlayerSessionResponse>> GetPlayerSessionsAsync(string playerId, int pageNumber = 1, int pageSize = 20);
        Task<bool> UpdateLastActivityAsync(string sessionId);
        Task<List<PlayerSessionResponse>> GetActiveSessionsAsync();
        Task CleanupInactiveSessionsAsync(TimeSpan inactivityThreshold);
        Task ResetPlayerStatsAsync(string playerId);
        Task<bool> EndSessionByPlayerIdAsync(string playerId);
    }

    public class PlayerSessionService : IPlayerSessionService
    {
        private readonly IMongoCollection<PlayerSession> _sessionCollection;
        private readonly IMongoCollection<PlayerStats> _statsCollection;
        private readonly ILogger<PlayerSessionService> _logger;

        public PlayerSessionService(IMongoDatabase database, ILogger<PlayerSessionService> logger)
        {
            _sessionCollection = database.GetCollection<PlayerSession>("playerSessions");
            _statsCollection = database.GetCollection<PlayerStats>("playerStats");
            _logger = logger;
        }

        public async Task<PlayerSessionResponse?> StartSessionAsync(StartSessionRequest request)
        {
            try
            {
                // End any existing active session for this player
                await EndExistingActiveSessionAsync(request.PlayerId);

                // Create new session
                var session = new PlayerSession
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    PlayerId = request.PlayerId,
                    Username = request.Username,
                    SessionStart = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,
                    IsActive = true,
                    CurrentBalance = request.InitialBalance,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _sessionCollection.InsertOneAsync(session);
                
                // Update or create player stats
                await UpdatePlayerStatsAsync(request.PlayerId, request.Username, request.InitialBalance);

                _logger.LogInformation("Started new session for player {PlayerId} ({Username})", request.PlayerId, request.Username);
                
                return MapToResponse(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting session for player {PlayerId}", request.PlayerId);
                throw;
            }
        }

        public async Task<PlayerSessionResponse?> GetActiveSessionAsync(string playerId)
        {
            try
            {
                // 🚨 VALIDATE PLAYER ID: Check if it's a valid MongoDB ObjectId
                if (string.IsNullOrEmpty(playerId) || playerId == "default" || playerId == "anonymous")
                {
                    _logger.LogWarning("❌ Invalid player ID: {PlayerId} - cannot query MongoDB", playerId);
                    return null;
                }
                
                if (!ObjectId.TryParse(playerId, out _))
                {
                    _logger.LogWarning("❌ Player ID is not a valid MongoDB ObjectId: {PlayerId}", playerId);
                    return null;
                }
                
                // 🔍 DEBUG: First check if ANY session exists for this player (active or inactive)
                var anySession = await _sessionCollection
                    .Find(s => s.PlayerId == playerId)
                    .FirstOrDefaultAsync();

                if (anySession != null)
                {
                    _logger.LogInformation("🔍 DEBUG: Found session for player {PlayerId}: SessionId={SessionId}, IsActive={IsActive}, Spins={Spins}, RTP={RTP:P2}", 
                        playerId, anySession.Id, anySession.IsActive, anySession.TotalSpins, anySession.TotalRtp);
                }
                else
                {
                    _logger.LogWarning("❌ DEBUG: No session found at all for player {PlayerId}", playerId);
                }

                // Now look for active session
                var session = await _sessionCollection
                    .Find(s => s.PlayerId == playerId && s.IsActive)
                    .FirstOrDefaultAsync();

                if (session != null)
                {
                    _logger.LogInformation("🔍 Retrieved ACTIVE session {SessionId}: Spins={Spins}, RTP={RTP:P2}, HitRate={HitRate:P2}, Bet={Bet:C}, Win={Win:C}", 
                        session.Id, session.TotalSpins, session.TotalRtp, session.HitRate, session.TotalBet, session.TotalWin);
                }
                else
                {
                    _logger.LogWarning("❌ No ACTIVE session found for player {PlayerId}", playerId);
                }

                return session != null ? MapToResponse(session) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active session for player {PlayerId}", playerId);
                throw;
            }
        }

        public async Task<PlayerSessionResponse?> GetSessionAsync(string sessionId)
        {
            try
            {
                var session = await _sessionCollection
                    .Find(s => s.Id == sessionId)
                    .FirstOrDefaultAsync();

                return session != null ? MapToResponse(session) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
                throw;
            }
        }

        public async Task<bool> EndSessionAsync(string sessionId)
        {
            try
            {
                // First get the session to calculate duration
                var session = await _sessionCollection.Find(s => s.Id == sessionId && s.IsActive).FirstOrDefaultAsync();
                if (session == null)
                {
                    return false;
                }

                var sessionEndTime = DateTime.UtcNow;
                var sessionDuration = sessionEndTime - session.SessionStart;
                
                var update = Builders<PlayerSession>.Update
                    .Set(s => s.SessionEnd, sessionEndTime)
                    .Set(s => s.SessionDuration, sessionDuration)
                    .Set(s => s.IsActive, false)
                    .Set(s => s.UpdatedAt, sessionEndTime);

                var result = await _sessionCollection.UpdateOneAsync(
                    s => s.Id == sessionId && s.IsActive,
                    update);

                if (result.ModifiedCount > 0)
                {
                    // Update player stats with final session data
                    var updatedSession = await _sessionCollection.Find(s => s.Id == sessionId).FirstOrDefaultAsync();
                    if (updatedSession != null)
                    {
                        await UpdatePlayerStatsFromSessionAsync(updatedSession);
                    }

                    _logger.LogInformation("Ended session {SessionId} - Duration: {Duration} minutes", 
                        sessionId, sessionDuration.TotalMinutes);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session {SessionId}", sessionId);
                throw;
            }
        }

        public async Task<bool> UpdateSessionStatsAsync(UpdateSessionStatsRequest request)
        {
            try
            {
                var session = await _sessionCollection.Find(s => s.Id == request.SessionId).FirstOrDefaultAsync();
                if (session == null || !session.IsActive)
                {
                    return false;
                }

                // Update session statistics - use the passed values from SpinController
                session.TotalBet += request.BetAmount;
                session.TotalWin += request.WinAmount;
                session.CurrentBalance = request.CurrentBalance;
                session.LastActivity = DateTime.UtcNow;
                session.UpdatedAt = DateTime.UtcNow;
                
                // 🚨 CRITICAL FIX: Use the TotalSpins and WinningSpins from the request (already updated by SpinController)
                session.TotalSpins = request.TotalSpins;
                session.WinningSpins = request.WinningSpins;

                if (request.WinAmount > session.MaxWin)
                {
                    session.MaxWin = request.WinAmount;
                }

                if (request.IsFreeSpin)
                {
                    session.FreeSpinsAwarded += request.FreeSpinsAwarded;
                }

                if (request.IsBonusTriggered)
                {
                    session.BonusesTriggered++;
                }

                // Calculate RTP and Hit Rate
                session.TotalRtp = session.TotalBet > 0 ? (double)(session.TotalWin / session.TotalBet) : 0;
                session.HitRate = session.TotalSpins > 0 ? (double)session.WinningSpins / session.TotalSpins : 0;

                // Update in database (TotalSpins already incremented by SpinController, just sync to DB)
                var update = Builders<PlayerSession>.Update
                    .Set(s => s.TotalSpins, session.TotalSpins) // Sync the already incremented value
                    .Set(s => s.TotalBet, session.TotalBet)
                    .Set(s => s.TotalWin, session.TotalWin)
                    .Set(s => s.TotalRtp, session.TotalRtp)
                    .Set(s => s.HitRate, session.HitRate)
                    .Set(s => s.WinningSpins, session.WinningSpins)
                    .Set(s => s.FreeSpinsAwarded, session.FreeSpinsAwarded)
                    .Set(s => s.BonusesTriggered, session.BonusesTriggered)
                    .Set(s => s.MaxWin, session.MaxWin)
                    .Set(s => s.CurrentBalance, session.CurrentBalance)
                    .Set(s => s.LastActivity, session.LastActivity)
                    .Set(s => s.UpdatedAt, session.UpdatedAt);

                _logger.LogInformation("🔄 Updating session {SessionId}: Spins={Spins}, RTP={RTP:P2}, HitRate={HitRate:P2}, Bet={Bet:C}, Win={Win:C}", 
                    request.SessionId, session.TotalSpins, session.TotalRtp, session.HitRate, request.BetAmount, request.WinAmount);

                var result = await _sessionCollection.UpdateOneAsync(
                    s => s.Id == request.SessionId,
                    update);

                _logger.LogInformation("✅ Database update result: Modified={Modified}, Matched={Matched}", 
                    result.ModifiedCount, result.MatchedCount);

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating session stats for session {SessionId}", request.SessionId);
                throw;
            }
        }

        public async Task<PlayerStatsResponse?> GetPlayerStatsAsync(string playerId)
        {
            try
            {
                // 🚨 VALIDATE PLAYER ID: Check if it's a valid MongoDB ObjectId
                if (string.IsNullOrEmpty(playerId) || playerId == "default" || playerId == "anonymous")
                {
                    _logger.LogWarning("❌ Invalid player ID: {PlayerId} - cannot query MongoDB", playerId);
                    return null;
                }
                
                if (!ObjectId.TryParse(playerId, out _))
                {
                    _logger.LogWarning("❌ Player ID is not a valid MongoDB ObjectId: {PlayerId}", playerId);
                    return null;
                }
                var stats = await _statsCollection
                    .Find(s => s.PlayerId == playerId)
                    .FirstOrDefaultAsync();

                return stats != null ? MapToStatsResponse(stats) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting player stats for {PlayerId}", playerId);
                throw;
            }
        }

        public async Task<List<PlayerSessionResponse>> GetPlayerSessionsAsync(string playerId, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                // 🚨 VALIDATE PLAYER ID: Check if it's a valid MongoDB ObjectId
                if (string.IsNullOrEmpty(playerId) || playerId == "default" || playerId == "anonymous")
                {
                    _logger.LogWarning("❌ Invalid player ID: {PlayerId} - cannot query MongoDB", playerId);
                    return new List<PlayerSessionResponse>();
                }
                
                if (!ObjectId.TryParse(playerId, out _))
                {
                    _logger.LogWarning("❌ Player ID is not a valid MongoDB ObjectId: {PlayerId}", playerId);
                    return new List<PlayerSessionResponse>();
                }
                var skip = (pageNumber - 1) * pageSize;
                var sessions = await _sessionCollection
                    .Find(s => s.PlayerId == playerId)
                    .SortByDescending(s => s.SessionStart)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync();

                return sessions.Select(MapToResponse).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting player sessions for {PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> UpdateLastActivityAsync(string sessionId)
        {
            try
            {
                var update = Builders<PlayerSession>.Update
                    .Set(s => s.LastActivity, DateTime.UtcNow)
                    .Set(s => s.UpdatedAt, DateTime.UtcNow);

                var result = await _sessionCollection.UpdateOneAsync(
                    s => s.Id == sessionId && s.IsActive,
                    update);

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last activity for session {SessionId}", sessionId);
                throw;
            }
        }

        public async Task<List<PlayerSessionResponse>> GetActiveSessionsAsync()
        {
            try
            {
                var sessions = await _sessionCollection
                    .Find(s => s.IsActive)
                    .SortByDescending(s => s.LastActivity)
                    .ToListAsync();

                return sessions.Select(MapToResponse).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active sessions");
                throw;
            }
        }

        public async Task CleanupInactiveSessionsAsync(TimeSpan inactivityThreshold)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.Subtract(inactivityThreshold);
                
                var update = Builders<PlayerSession>.Update
                    .Set(s => s.SessionEnd, DateTime.UtcNow)
                    .Set(s => s.IsActive, false)
                    .Set(s => s.UpdatedAt, DateTime.UtcNow);

                var result = await _sessionCollection.UpdateManyAsync(
                    s => s.IsActive && s.LastActivity < cutoffTime,
                    update);

                if (result.ModifiedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} inactive sessions", result.ModifiedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up inactive sessions");
                throw;
            }
        }

        private async Task EndExistingActiveSessionAsync(string playerId)
        {
            try
            {
                var update = Builders<PlayerSession>.Update
                    .Set(s => s.SessionEnd, DateTime.UtcNow)
                    .Set(s => s.IsActive, false)
                    .Set(s => s.UpdatedAt, DateTime.UtcNow);

                await _sessionCollection.UpdateManyAsync(
                    s => s.PlayerId == playerId && s.IsActive,
                    update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending existing active session for player {PlayerId}", playerId);
                throw;
            }
        }

        private async Task UpdatePlayerStatsAsync(string playerId, string username, decimal initialBalance)
        {
            try
            {
                var existingStats = await _statsCollection.Find(s => s.PlayerId == playerId).FirstOrDefaultAsync();
                
                if (existingStats == null)
                {
                    // Create new player stats
                    var newStats = new PlayerStats
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        PlayerId = playerId,
                        Username = username,
                        CurrentBalance = initialBalance,
                        FirstSessionDate = DateTime.UtcNow,
                        LastSessionDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _statsCollection.InsertOneAsync(newStats);
                }
                else
                {
                    // Update existing stats
                    var update = Builders<PlayerStats>.Update
                        .Set(s => s.LastSessionDate, DateTime.UtcNow)
                        .Set(s => s.CurrentBalance, initialBalance)
                        .Set(s => s.UpdatedAt, DateTime.UtcNow);

                    await _statsCollection.UpdateOneAsync(s => s.PlayerId == playerId, update);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating player stats for {PlayerId}", playerId);
                throw;
            }
        }

        private async Task UpdatePlayerStatsFromSessionAsync(PlayerSession session)
        {
            try
            {
                var stats = await _statsCollection.Find(s => s.PlayerId == session.PlayerId).FirstOrDefaultAsync();
                if (stats == null) 
                {
                    // Create new player stats if they don't exist
                    stats = new PlayerStats
                    {
                        PlayerId = session.PlayerId,
                        Username = session.Username,
                        TotalSessions = 1,
                        TotalSpins = session.TotalSpins,
                        TotalBet = session.TotalBet,
                        TotalWin = session.TotalWin,
                        TotalWinningSpins = session.WinningSpins,
                        TotalFreeSpinsAwarded = session.FreeSpinsAwarded,
                        TotalBonusesTriggered = session.BonusesTriggered,
                        CurrentBalance = session.CurrentBalance,
                        MaxWinEver = session.MaxWin,
                        LastSessionDate = DateTime.UtcNow,
                        LastLoginDate = session.SessionStart, // Set last login date to session start time
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    
                    // Calculate lifetime RTP and Hit Rate
                    stats.LifetimeRtp = stats.TotalBet > 0 ? (double)(stats.TotalWin / stats.TotalBet) : 0;
                    stats.LifetimeHitRate = stats.TotalSpins > 0 ? (double)stats.TotalWinningSpins / stats.TotalSpins : 0;
                    
                    await _statsCollection.InsertOneAsync(stats);
                    return;
                }

                // Update lifetime statistics (only increment session count when session ends)
                stats.TotalSessions++;
                stats.TotalSpins += session.TotalSpins;
                stats.TotalBet += session.TotalBet;
                stats.TotalWin += session.TotalWin;
                stats.TotalWinningSpins += session.WinningSpins;
                stats.TotalFreeSpinsAwarded += session.FreeSpinsAwarded;
                stats.TotalBonusesTriggered += session.BonusesTriggered;
                stats.CurrentBalance = session.CurrentBalance;
                stats.LastSessionDate = DateTime.UtcNow;
                stats.LastLoginDate = session.SessionStart; // Set last login date to session start time
                stats.UpdatedAt = DateTime.UtcNow;

                if (session.MaxWin > stats.MaxWinEver)
                {
                    stats.MaxWinEver = session.MaxWin;
                }

                // Calculate lifetime RTP and Hit Rate
                stats.LifetimeRtp = stats.TotalBet > 0 ? (double)(stats.TotalWin / stats.TotalBet) : 0;
                stats.LifetimeHitRate = stats.TotalSpins > 0 ? (double)stats.TotalWinningSpins / stats.TotalSpins : 0;

                var update = Builders<PlayerStats>.Update
                    .Set(s => s.TotalSessions, stats.TotalSessions)
                    .Set(s => s.TotalSpins, stats.TotalSpins)
                    .Set(s => s.TotalBet, stats.TotalBet)
                    .Set(s => s.TotalWin, stats.TotalWin)
                    .Set(s => s.LifetimeRtp, stats.LifetimeRtp)
                    .Set(s => s.LifetimeHitRate, stats.LifetimeHitRate)
                    .Set(s => s.TotalWinningSpins, stats.TotalWinningSpins)
                    .Set(s => s.TotalFreeSpinsAwarded, stats.TotalFreeSpinsAwarded)
                    .Set(s => s.TotalBonusesTriggered, stats.TotalBonusesTriggered)
                    .Set(s => s.MaxWinEver, stats.MaxWinEver)
                    .Set(s => s.CurrentBalance, stats.CurrentBalance)
                    .Set(s => s.LastSessionDate, stats.LastSessionDate)
                    .Set(s => s.LastLoginDate, stats.LastLoginDate)
                    .Set(s => s.UpdatedAt, stats.UpdatedAt);

                await _statsCollection.UpdateOneAsync(s => s.PlayerId == session.PlayerId, update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating player stats from session {SessionId}", session.Id);
                throw;
            }
        }

        private static PlayerSessionResponse MapToResponse(PlayerSession session)
        {
            var response = new PlayerSessionResponse
            {
                SessionId = session.Id,
                PlayerId = session.PlayerId,
                Username = session.Username,
                SessionStart = session.SessionStart,
                SessionEnd = session.SessionEnd,
                IsActive = session.IsActive,
                LastActivity = session.LastActivity,
                TotalSpins = session.TotalSpins,
                TotalBet = session.TotalBet,
                TotalWin = session.TotalWin,
                TotalRtp = session.TotalRtp,
                HitRate = session.HitRate,
                WinningSpins = session.WinningSpins,
                FreeSpinsAwarded = session.FreeSpinsAwarded,
                BonusesTriggered = session.BonusesTriggered,
                MaxWin = session.MaxWin,
                CurrentBalance = session.CurrentBalance,
                SessionDuration = session.SessionDuration
            };

            // Debug logging
            // PERFORMANCE: Console.WriteLine removed for speed
            // Console.WriteLine($"🔍 MapToResponse: SessionId={response.SessionId}, Spins={response.TotalSpins}, RTP={response.TotalRtp:P2}, HitRate={response.HitRate:P2}");

            return response;
        }

        private static PlayerStatsResponse MapToStatsResponse(PlayerStats stats)
        {
            return new PlayerStatsResponse
            {
                PlayerId = stats.PlayerId,
                Username = stats.Username,
                TotalSessions = stats.TotalSessions,
                TotalSpins = stats.TotalSpins,
                TotalBet = stats.TotalBet,
                TotalWin = stats.TotalWin,
                LifetimeRtp = stats.LifetimeRtp,
                LifetimeHitRate = stats.LifetimeHitRate,
                TotalWinningSpins = stats.TotalWinningSpins,
                TotalFreeSpinsAwarded = stats.TotalFreeSpinsAwarded,
                TotalBonusesTriggered = stats.TotalBonusesTriggered,
                MaxWinEver = stats.MaxWinEver,
                CurrentBalance = stats.CurrentBalance,
                FirstSessionDate = stats.FirstSessionDate,
                LastSessionDate = stats.LastSessionDate,
                LastLoginDate = stats.LastLoginDate
            };
        }

        public async Task ResetPlayerStatsAsync(string playerId)
        {
            try
            {
                // Delete existing player stats
                await _statsCollection.DeleteManyAsync(s => s.PlayerId == playerId);
                
                // Delete all sessions for this player
                await _sessionCollection.DeleteManyAsync(s => s.PlayerId == playerId);
                
                _logger.LogInformation("Reset all stats and sessions for player {PlayerId}", playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting player stats for {PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> EndSessionByPlayerIdAsync(string playerId)
        {
            try
            {
                var activeSession = await _sessionCollection
                    .Find(s => s.PlayerId == playerId && s.IsActive)
                    .FirstOrDefaultAsync();

                if (activeSession == null)
                {
                    return false;
                }

                return await EndSessionAsync(activeSession.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session for player {PlayerId}", playerId);
                throw;
            }
        }
    }
}
