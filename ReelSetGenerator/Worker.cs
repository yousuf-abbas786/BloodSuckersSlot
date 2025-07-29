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
            
            // Load GameConfig from appsettings
            _config = GameConfigLoader.LoadFromConfiguration(_configuration);
            _logger.LogInformation($"Loaded GameConfig from appsettings - RTP Target: {_config.RtpTarget:P1}, Hit Rate Target: {_config.TargetHitRate:P1}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int batchSize = 1000;
            int total = 100000;
            int written = 0;
            int betAmount = _config.BaseBetForFreeSpins; // Use configuration instead of hardcoded value
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
