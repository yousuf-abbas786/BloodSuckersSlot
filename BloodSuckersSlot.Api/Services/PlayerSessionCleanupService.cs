using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BloodSuckersSlot.Api.Services
{
    public class PlayerSessionCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlayerSessionCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5); // Clean up every 5 minutes
        private readonly TimeSpan _inactivityThreshold = TimeSpan.FromMinutes(30); // Sessions inactive for 30 minutes

        public PlayerSessionCleanupService(IServiceProvider serviceProvider, ILogger<PlayerSessionCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🧹 PlayerSessionCleanupService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var playerSessionService = scope.ServiceProvider.GetRequiredService<IPlayerSessionService>();
                    var playerSpinSessionService = scope.ServiceProvider.GetRequiredService<IPlayerSpinSessionService>();

                    // Clean up inactive sessions
                    await playerSessionService.CleanupInactiveSessionsAsync(_inactivityThreshold);
                    
                    // Clean up inactive SpinLogicHelper sessions
                    playerSpinSessionService.CleanupInactiveSessions(_inactivityThreshold);
                    var activeSessionCount = playerSpinSessionService.GetActiveSessionCount();
                    _logger.LogDebug("Player session cleanup completed. Active SpinLogicHelper sessions: {Count}", activeSessionCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during player session cleanup");
                }

                await Task.Delay(_cleanupInterval, stoppingToken);
            }

            _logger.LogInformation("PlayerSessionCleanupService stopped");
        }
    }
}
