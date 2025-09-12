using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BloodSuckersSlot.Api.Services
{
    /// <summary>
    /// Background service that periodically updates global RTP statistics
    /// to reduce database load during spins
    /// </summary>
    public class GlobalRtpUpdateService : BackgroundService
    {
        private readonly IGlobalRtpBalancingService _globalRtpBalancingService;
        private readonly ILogger<GlobalRtpUpdateService> _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(2); // Update every 2 seconds

        public GlobalRtpUpdateService(
            IGlobalRtpBalancingService globalRtpBalancingService,
            ILogger<GlobalRtpUpdateService> logger)
        {
            _globalRtpBalancingService = globalRtpBalancingService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 GlobalRtpUpdateService started - updating global RTP stats every {Interval}s", _updateInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Update global RTP statistics in background
                    await _globalRtpBalancingService.UpdateGlobalRtpStatsAsync();
                    
                    _logger.LogDebug("📊 Background global RTP stats updated");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error updating global RTP stats in background service");
                }

                // Wait for next update cycle
                await Task.Delay(_updateInterval, stoppingToken);
            }

            _logger.LogInformation("🛑 GlobalRtpUpdateService stopped");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 Stopping GlobalRtpUpdateService...");
            await base.StopAsync(cancellationToken);
        }
    }
}
