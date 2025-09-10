using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using MongoDB.Driver;
using MongoDB.Bson;
using Shared;
using System.Text.Json;
using System.Diagnostics;
using BloodSuckersSlot.Api.Models;
using BloodSuckersSlot.Api.Controllers;

namespace BloodSuckersSlot.Api.Services
{
    public class AutoSpinService : BackgroundService
    {
        private readonly ILogger<AutoSpinService> _logger;
        private readonly ConcurrentDictionary<string, AutoSpinSession> _activeSessions = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly PerformanceSettings _performanceSettings;
        private readonly MongoDbSettings _mongoDbSettings;
        private static GameConfig _config = null!;

        public AutoSpinService(ILogger<AutoSpinService> logger, IServiceProvider serviceProvider,
            PerformanceSettings performanceSettings, MongoDbSettings mongoDbSettings, IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _performanceSettings = performanceSettings;
            _mongoDbSettings = mongoDbSettings;
            _config = GameConfigLoader.LoadFromConfiguration(configuration);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🎰 AutoSpinService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessActiveSessions(stoppingToken);
                    await Task.Delay(100, stoppingToken); // Check every 100ms
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AutoSpinService execution loop");
                    await Task.Delay(1000, stoppingToken); // Wait 1 second on error
                }
            }

            _logger.LogInformation("🎰 AutoSpinService stopped");
        }

        private async Task ProcessActiveSessions(CancellationToken cancellationToken)
        {
            var sessionsToProcess = _activeSessions.Values
                .Where(s => s.IsActive && s.RemainingSpins > 0)
                .ToList();

            foreach (var session in sessionsToProcess)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await ProcessSession(session);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing auto-spin session {session.Id}");
                    // Mark session as inactive on error
                    session.IsActive = false;
                    session.StoppedAt = DateTime.UtcNow;
                }
            }

            // Clean up completed sessions
            var completedSessions = _activeSessions.Values
                .Where(s => !s.IsActive || s.RemainingSpins <= 0)
                .ToList();

            foreach (var session in completedSessions)
            {
                _activeSessions.TryRemove(session.Id, out _);
                _logger.LogInformation($"🧹 Cleaned up completed auto-spin session {session.Id}");
            }
        }

        private async Task ProcessSession(AutoSpinSession session)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastSpin = now - session.LastSpinTime;

            // Check if enough time has passed for the next spin
            if (timeSinceLastSpin.TotalMilliseconds < session.SpinDelayMs)
            {
                return; // Not time for next spin yet
            }

            // Perform the spin
            await ExecuteSpin(session);
            
            // Update session
            session.LastSpinTime = now;
            session.RemainingSpins--;
            session.TotalBets += session.BetAmount;

            // Check if session is complete
            if (session.RemainingSpins <= 0)
            {
                session.IsActive = false;
                session.StoppedAt = now;
                _logger.LogInformation($"✅ Auto-spin session {session.Id} completed: {session.SpinCount} spins, {session.TotalWins:C} total wins");
            }
        }

        private async Task ExecuteSpin(AutoSpinSession session)
        {
            try
            {
                // Use the same spin logic as SpinController but without the HTTP context
                var spinRequest = new SpinRequestDto
                {
                    Level = session.Level,
                    CoinValue = session.CoinValue,
                    BetAmount = (int)session.BetAmount
                };

                // Execute spin logic directly
                var result = await PerformSpinInternal(spinRequest);
                
                if (result != null)
                {
                    // Extract win amount from result
                    if (result.TryGetValue("monetaryPayout", out var payoutObj) && payoutObj is decimal monetaryPayout)
                    {
                        session.TotalWins += monetaryPayout;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing spin for session {session.Id}");
                throw;
            }
        }

        private async Task<Dictionary<string, object>?> PerformSpinInternal(SpinRequestDto request)
        {
            try
            {
                // Simplified spin logic without HTTP context dependencies
                var betInCoins = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, request.Level);
                var totalBet = BettingSystem.CalculateTotalBet(_config.BaseBetPerLevel, request.Level, request.CoinValue);

                // Get reel sets from database
                var reelSets = await GetReelSetsFromDatabase();
                if (reelSets == null || !reelSets.Any())
                {
                    _logger.LogError("No reel sets available for spin");
                    return null;
                }

                // Use the same spin logic as SpinController
                var (result, grid, chosenSet, winningLines) = SpinLogicHelper.SpinWithReelSets(_config, betInCoins, reelSets);

                if (result == null || grid == null || chosenSet == null)
                {
                    _logger.LogError("Spin evaluation failed");
                    return null;
                }

                // Calculate monetary payout
                var monetaryPayout = BettingSystem.CalculatePayout((int)result.TotalWin, request.CoinValue);

                return new Dictionary<string, object>
                {
                    ["monetaryPayout"] = monetaryPayout,
                    ["totalWin"] = result.TotalWin,
                    ["grid"] = grid,
                    ["result"] = result,
                    ["chosenReelSet"] = chosenSet,
                    ["winningLines"] = winningLines
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PerformSpinInternal");
                return null;
            }
        }

        private async Task<List<ReelSet>?> GetReelSetsFromDatabase()
        {
            try
            {
                // Get MongoDB collection from service provider
                var mongoClient = _serviceProvider.GetRequiredService<IMongoClient>();
                var database = mongoClient.GetDatabase(_mongoDbSettings.Database);
                var collection = database.GetCollection<BsonDocument>("reelsets");
                
                // Get multiple reel sets from database
                var filter = Builders<BsonDocument>.Filter.Empty;
                var reelSetDocs = await collection.Find(filter).Limit(100).ToListAsync();
                
                if (reelSetDocs != null && reelSetDocs.Any())
                {
                    var reelSets = new List<ReelSet>();
                    foreach (var doc in reelSetDocs)
                    {
                        var reelSetJson = doc.ToJson();
                        var reelSet = JsonSerializer.Deserialize<ReelSet>(reelSetJson);
                        if (reelSet != null)
                        {
                            reelSets.Add(reelSet);
                        }
                    }
                    return reelSets;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving reel sets");
                return null;
            }
        }

        public void StartSession(AutoSpinSession session)
        {
            session.LastSpinTime = DateTime.UtcNow;
            _activeSessions[session.Id] = session;
            _logger.LogInformation($"🎰 Started auto-spin session {session.Id}: {session.SpinCount} spins, {session.SpinDelayMs}ms delay");
        }

        public void StopSession(string sessionId)
        {
            if (_activeSessions.TryGetValue(sessionId, out var session))
            {
                session.IsActive = false;
                session.StoppedAt = DateTime.UtcNow;
                _logger.LogInformation($"🛑 Stopped auto-spin session {sessionId}");
            }
        }

        public AutoSpinSession? GetSession(string sessionId)
        {
            _activeSessions.TryGetValue(sessionId, out var session);
            return session;
        }

        public List<AutoSpinSession> GetAllSessions()
        {
            return _activeSessions.Values.ToList();
        }
    }

    public class AutoSpinSession
    {
        public string Id { get; set; } = "";
        public string PlayerId { get; set; } = "";
        public int SpinCount { get; set; }
        public int RemainingSpins { get; set; }
        public int SpinDelayMs { get; set; }
        public int Level { get; set; }
        public decimal CoinValue { get; set; }
        public decimal BetAmount { get; set; }
        public bool IsActive { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastSpinTime { get; set; }
        public DateTime? StoppedAt { get; set; }
        public decimal TotalWins { get; set; }
        public decimal TotalBets { get; set; }
    }

    public class SpinRequestDto
    {
        public int BetAmount { get; set; } = 25; // Legacy support
        public int Level { get; set; } = 1;
        public decimal CoinValue { get; set; } = 0.10m;
    }
}