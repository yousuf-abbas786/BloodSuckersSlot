using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReelSetGenerator
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly GameConfig _config;
        private readonly IConfiguration _configuration;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            var connectionString = _configuration["MongoDb:ConnectionString"];
            var dbName = _configuration["MongoDb:Database"];
            var client = new MongoClient(connectionString);
            var db = client.GetDatabase(dbName);
            _collection = db.GetCollection<BsonDocument>("reelsets");

            // Ensure indexes exist
            var indexKeys = new List<CreateIndexModel<BsonDocument>>
            {
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("name")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("tag")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("expectedRtp")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("estimatedHitRate"))
            };
            _collection.Indexes.CreateMany(indexKeys);
            _config = GameConfig.CreateBalanced(); // Or load from config
            // Example paylines (replace with your actual paylines)
            _config.Paylines = new List<int[]>
            {
                new[] { 1, 1, 1, 1, 1 },
                new[] { 0, 0, 0, 0, 0 },
                new[] { 2, 2, 2, 2, 2 },
                new[] { 0, 1, 2, 1, 0 },
                new[] { 2, 1, 0, 1, 2 },
                new[] { 0, 0, 1, 2, 2 },
                new[] { 2, 2, 1, 0, 0 },
                new[] { 0, 1, 1, 1, 0 },
                new[] { 2, 1, 1, 1, 2 },
                new[] { 1, 0, 1, 2, 1 },
                new[] { 1, 2, 1, 0, 1 },
                new[] { 0, 1, 0, 1, 0 },
                new[] { 2, 1, 2, 1, 2 },
                new[] { 1, 1, 0, 1, 1 },
                new[] { 1, 1, 2, 1, 1 },
                new[] { 0, 1, 2, 2, 2 },
                new[] { 2, 1, 0, 0, 0 },
                new[] { 1, 2, 2, 2, 1 },
                new[] { 1, 0, 0, 0, 1 },
                new[] { 0, 0, 1, 1, 2 },
                new[] { 2, 2, 1, 1, 0 },
                new[] { 0, 1, 2, 1, 2 },
                new[] { 2, 1, 0, 1, 0 },
                new[] { 1, 0, 1, 2, 0 },
                new[] { 1, 2, 1, 0, 2 }
            };
            // Example symbol config (replace with your actual symbols)
            _config.Symbols = new Dictionary<string, SymbolConfig>
            {
                ["SYM0"] = new SymbolConfig { SymbolId = "SYM0", IsScatter = true, Payouts = new Dictionary<int, double> { [2] = 2, [3] = 4, [4] = 25, [5] = 100 } },
                ["SYM1"] = new SymbolConfig { SymbolId = "SYM1", IsWild = true, Payouts = new Dictionary<int, double> { [2] = 0.5, [3] = 20, [4] = 200, [5] = 750 } },
                ["SYM2"] = new SymbolConfig { SymbolId = "SYM2", IsBonus = true },
                ["SYM3"] = new SymbolConfig { SymbolId = "SYM3", Payouts = new Dictionary<int, double> { [3] = 5, [4] = 10, [5] = 50 } },
                ["SYM4"] = new SymbolConfig { SymbolId = "SYM4", Payouts = new Dictionary<int, double> { [3] = 5, [4] = 10, [5] = 50 } },
                ["SYM5"] = new SymbolConfig { SymbolId = "SYM5", Payouts = new Dictionary<int, double> { [3] = 1.5, [4] = 7.5, [5] = 25 } },
                ["SYM6"] = new SymbolConfig { SymbolId = "SYM6", Payouts = new Dictionary<int, double> { [3] = 1.5, [4] = 7.5, [5] = 25 } },
                ["SYM7"] = new SymbolConfig { SymbolId = "SYM7", Payouts = new Dictionary<int, double> { [3] = 0.5, [4] = 5, [5] = 10 } },
                ["SYM8"] = new SymbolConfig { SymbolId = "SYM8", Payouts = new Dictionary<int, double> { [3] = 0.5, [4] = 2.5, [5] = 10 } },
                ["SYM9"] = new SymbolConfig { SymbolId = "SYM9", Payouts = new Dictionary<int, double> { [3] = 0.2, [4] = 1.5, [5] = 7.5 } },
                ["SYM10"] = new SymbolConfig { SymbolId = "SYM10", Payouts = new Dictionary<int, double> { [3] = 0.2, [4] = 1.5, [5] = 7.5 } }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int batchSize = 1000;
            int total = 100000;
            int written = 0;
            int betAmount = 25;
            int processed = 0;
            int highRtpCount = 0, midRtpCount = 0, lowRtpCount = 0;
            var startTime = DateTime.UtcNow;
            while (written < total && !stoppingToken.IsCancellationRequested)
            {
                var batch = new List<BsonDocument>();
                var reelSets = Shared.ReelSetGenerator.GenerateRandomReelSets(batchSize);
                foreach (var set in reelSets)
                {
                    var mcStart = DateTime.UtcNow;
                    (double rtp, double hitRate) = Shared.ReelSetGenerator.MonteCarloSimulate(set, _config.Paylines, _config.MonteCarloSpins, betAmount);
                    var mcElapsed = DateTime.UtcNow - mcStart;
                    Console.WriteLine($"Monte Carlo for {set.Name} took {mcElapsed.TotalMilliseconds:F1} ms (RTP: {rtp:F4}, HitRate: {hitRate:F4})");
                    set.ExpectedRtp = rtp;
                    set.EstimatedHitRate = hitRate;
                    var doc = new BsonDocument
                    {
                        { "name", set.Name },
                        { "tag", set.Name.StartsWith("HighRtp") ? "HighRtp" : set.Name.StartsWith("MidRtp") ? "MidRtp" : "LowRtp" },
                        { "expectedRtp", set.ExpectedRtp },
                        { "estimatedHitRate", set.EstimatedHitRate },
                        { "reels", new BsonArray(set.Reels.ConvertAll(strip => new BsonArray(strip))) }
                    };
                    batch.Add(doc);
                    processed++;
                    if (set.Name.StartsWith("HighRtp")) highRtpCount++;
                    else if (set.Name.StartsWith("MidRtp")) midRtpCount++;
                    else lowRtpCount++;
                    var elapsed = DateTime.UtcNow - startTime;
                    Console.WriteLine($"Processed {processed}/{total} in total... Elapsed: {elapsed.TotalSeconds:F1} seconds");
                }
                if (batch.Count > 0)
                {
                    await _collection.InsertManyAsync(batch, cancellationToken: stoppingToken);
                    written += batch.Count;
                    _logger.LogInformation($"Inserted {written}/{total} reel sets...");
                }
            }
            _logger.LogInformation($"Reel set generation complete. HighRtp: {highRtpCount}, MidRtp: {midRtpCount}, LowRtp: {lowRtpCount}");
        }
    }
}
