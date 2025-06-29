using BloodSuckersSlot.Mongo;

using Microsoft.Extensions.Options;

namespace BloodSuckersSlot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly SlotEngine _engine;
        private readonly PlayerSession _playerSession;

        public Worker(ILogger<Worker> logger, IOptions<GameConfig> config, GlobalConfig globalConfig, ShopConfig shopConfig, EngineConfig engineConfig, PlayerSession playerSession, ShopState shopState)
        {
            _logger = logger;
            _playerSession = playerSession;

            _engine = new SlotEngine(config.Value, globalConfig, shopConfig, engineConfig, playerSession, shopState);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int spinCount = 0;

            while (!stoppingToken.IsCancellationRequested && spinCount < 1000)
            {
                _engine.Spin(_playerSession.BetAmount);
                spinCount++;

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
