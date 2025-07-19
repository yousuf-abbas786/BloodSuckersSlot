using Microsoft.Extensions.Options;

namespace BloodSuckersSlot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly SlotEngine _engine;

        public Worker(ILogger<Worker> logger, GameConfig config)
        {
            _logger = logger;
            _engine = new SlotEngine(config);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int spinCount = 0;

            while (!stoppingToken.IsCancellationRequested && spinCount < 1000)
            {
                _engine.Spin(25);
                spinCount++;

                //await Task.Delay(500, stoppingToken);
            }
        }
    }
}
