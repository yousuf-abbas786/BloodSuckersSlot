using Microsoft.Extensions.Options;
using Shared;

namespace BloodSuckersSlot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly SlotEngine _engine;
        private readonly GameConfig _config;

        public Worker(ILogger<Worker> logger, GameConfig config)
        {
            _logger = logger;
            _config = config;
            _engine = new SlotEngine(config);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int spinCount = 0;

            while (!stoppingToken.IsCancellationRequested && spinCount < 1000)
            {
                int betInCoins = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, _config.DefaultLevel);
                _engine.Spin(betInCoins, _config.DefaultLevel, _config.DefaultCoinValue);
                spinCount++;

                //await Task.Delay(500, stoppingToken);
            }
        }
    }
}
