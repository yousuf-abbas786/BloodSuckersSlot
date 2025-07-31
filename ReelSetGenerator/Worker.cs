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
using System.Linq;
using System.Collections.Concurrent;

namespace ReelSetGenerator
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly GameConfig _config;
        private readonly IConfiguration _configuration;
        private readonly int _maxDegreeOfParallelism;

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
            _maxDegreeOfParallelism = Environment.ProcessorCount; // Use all available CPU cores
            
            _logger.LogInformation($"Loaded GameConfig from appsettings - RTP Target: {_config.RtpTarget:P1}, Hit Rate Target: {_config.TargetHitRate:P1}");
            _logger.LogInformation($"Using {_maxDegreeOfParallelism} parallel threads for processing");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int totalReelSets = 1000000; // 1 million reel sets
            int batchSize = 10000; // Larger batch size for better performance
            int processed = 0;
            int ultraHighRtpCount = 0, highRtpCount = 0, midRtpCount = 0, lowRtpCount = 0, ultraLowRtpCount = 0;
            var startTime = DateTime.UtcNow;
            var betInCoins = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, _config.DefaultLevel);

            _logger.LogInformation($"Starting generation of {totalReelSets:N0} reel sets with {_maxDegreeOfParallelism} parallel threads");

            // Process in batches for memory efficiency
            for (int batchStart = 0; batchStart < totalReelSets; batchStart += batchSize)
            {
                if (stoppingToken.IsCancellationRequested) break;

                int currentBatchSize = Math.Min(batchSize, totalReelSets - batchStart);
                _logger.LogInformation($"Processing batch {batchStart / batchSize + 1}/{(totalReelSets + batchSize - 1) / batchSize} ({currentBatchSize:N0} reel sets)");

                // Generate reel sets in parallel
                var reelSets = await GenerateReelSetsParallelAsync(currentBatchSize, stoppingToken);

                // Process Monte Carlo simulations in parallel
                var processedReelSets = await ProcessMonteCarloParallelAsync(reelSets, betInCoins, stoppingToken);

                // Count by type for reporting
                foreach (var set in processedReelSets)
                {
                    if (set.Name.StartsWith("UltraHighRtp")) ultraHighRtpCount++;
                    else if (set.Name.StartsWith("HighRtp")) highRtpCount++;
                    else if (set.Name.StartsWith("MidRtp")) midRtpCount++;
                    else if (set.Name.StartsWith("LowRtp")) lowRtpCount++;
                    else if (set.Name.StartsWith("UltraLowRtp")) ultraLowRtpCount++;
                }

                // Convert to BsonDocuments and insert in bulk
                var documents = processedReelSets.Select(set => new BsonDocument
                {
                    { "name", set.Name },
                    { "tag", GetRtpTag(set.Name) },
                    { "expectedRtp", set.ExpectedRtp },
                    { "estimatedHitRate", set.EstimatedHitRate },
                    { "reels", new BsonArray(set.Reels.ConvertAll(strip => new BsonArray(strip))) }
                }).ToList();

                await _collection.InsertManyAsync(documents, cancellationToken: stoppingToken);
                processed += processedReelSets.Count;

                var elapsed = DateTime.UtcNow - startTime;
                var rate = processed / elapsed.TotalSeconds;
                var eta = TimeSpan.FromSeconds((totalReelSets - processed) / rate);

                _logger.LogInformation($"Processed {processed:N0}/{totalReelSets:N0} reel sets " +
                                    $"(Rate: {rate:F0}/sec, Elapsed: {elapsed.TotalMinutes:F1}min, ETA: {eta.TotalMinutes:F1}min)");
            }

            var totalElapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation($"Reel set generation complete in {totalElapsed.TotalMinutes:F1} minutes");
            _logger.LogInformation($"Final counts - UltraHighRtp: {ultraHighRtpCount:N0}, HighRtp: {highRtpCount:N0}, MidRtp: {midRtpCount:N0}, LowRtp: {lowRtpCount:N0}, UltraLowRtp: {ultraLowRtpCount:N0}");
        }

        private async Task<List<ReelSet>> GenerateReelSetsParallelAsync(int count, CancellationToken cancellationToken)
        {
            var reelSets = new ConcurrentBag<ReelSet>();
            
            await Task.Run(() =>
            {
                Parallel.For(0, count, new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                }, i =>
                {
                    var reelSet = GenerateSingleReelSet(i);
                    reelSets.Add(reelSet);
                });
            }, cancellationToken);

            return reelSets.ToList();
        }

        private ReelSet GenerateSingleReelSet(int index)
        {
            var rng = new Random(Guid.NewGuid().GetHashCode()); // Thread-safe random
            var reels = new List<List<string>>();
            
            // Base weights optimized for 88% RTP target
            var symbolWeights = new Dictionary<string, int>
            {
                ["SYM0"] = 2,   // Scatter - minimal for 88% RTP
                ["SYM1"] = 3,   // Wild - moderate for 88% RTP
                ["SYM2"] = 8,   // Bonus - moderate for 88% RTP
                ["SYM3"] = 12,  // High value (500) - moderate weight
                ["SYM4"] = 15,  // Medium-high value (250)
                ["SYM5"] = 15,  // Medium-high value (250)
                ["SYM6"] = 18,  // Medium value (125)
                ["SYM7"] = 20,  // Medium value (100)
                ["SYM8"] = 20,  // Medium value (100)
                ["SYM9"] = 25,  // Low value (75)
                ["SYM10"] = 25  // Low value (75)
            };

            string tag;
            string rtpTier;
            
            // Optimized 5-tier distribution for 88% RTP, 40% hit rate
            if (index < 1000000 * 0.15)
            {
                tag = "UltraLowRtp";
                rtpTier = "UltraLow";
                // Purpose: Provide very conservative sets (80-85% RTP, 35-40% hit rate)
                symbolWeights["SYM3"] = 5;   // Reduce high-value symbols
                symbolWeights["SYM4"] = 8;
                symbolWeights["SYM5"] = 8;
                symbolWeights["SYM6"] = 12;
                symbolWeights["SYM1"] = 0;   // No wilds
                symbolWeights["SYM0"] = 1;   // Minimal scatters
                symbolWeights["SYM2"] = 6;   // Fewer bonus triggers
                symbolWeights["SYM8"] = 35;  // Increase low-value symbols
                symbolWeights["SYM9"] = 35;
                symbolWeights["SYM10"] = 35;
            }
            else if (index < 1000000 * 0.35)
            {
                tag = "LowRtp";
                rtpTier = "Low";
                // Purpose: Conservative sets (85-87% RTP, 38-42% hit rate)
                symbolWeights["SYM3"] = 8;   // Moderate high-value symbols
                symbolWeights["SYM4"] = 12;
                symbolWeights["SYM5"] = 12;
                symbolWeights["SYM6"] = 15;
                symbolWeights["SYM1"] = 1;   // Few wilds
                symbolWeights["SYM0"] = 2;   // Few scatters
                symbolWeights["SYM2"] = 8;   // Moderate bonus triggers
                symbolWeights["SYM8"] = 25;  // Moderate low-value symbols
                symbolWeights["SYM9"] = 25;
                symbolWeights["SYM10"] = 25;
            }
            else if (index < 1000000 * 0.65)
            {
                tag = "MidRtp";
                rtpTier = "Mid";
                // Purpose: Core balanced sets (87-89% RTP, 40-42% hit rate) - TARGET RANGE
                // Use base weights - this is the sweet spot
                // No adjustments needed - base weights are optimized for this range
            }
            else if (index < 1000000 * 0.85)
            {
                tag = "HighRtp";
                rtpTier = "High";
                // Purpose: Aggressive sets (89-92% RTP, 38-42% hit rate)
                symbolWeights["SYM3"] = 18;  // Increase high-value symbols
                symbolWeights["SYM4"] = 20;
                symbolWeights["SYM5"] = 20;
                symbolWeights["SYM6"] = 22;
                symbolWeights["SYM1"] = 5;   // More wilds
                symbolWeights["SYM0"] = 4;   // More scatters
                symbolWeights["SYM2"] = 12;  // More bonus triggers
                symbolWeights["SYM8"] = 15;  // Reduce low-value symbols
                symbolWeights["SYM9"] = 15;
                symbolWeights["SYM10"] = 15;
            }
            else
            {
                tag = "UltraHighRtp";
                rtpTier = "UltraHigh";
                // Purpose: High volatility sets (92-95% RTP, 35-40% hit rate)
                symbolWeights["SYM3"] = 25;  // High-value symbols dominate
                symbolWeights["SYM4"] = 22;
                symbolWeights["SYM5"] = 20;
                symbolWeights["SYM6"] = 18;
                symbolWeights["SYM1"] = 8;   // Many wilds
                symbolWeights["SYM0"] = 6;   // Many scatters
                symbolWeights["SYM2"] = 15;  // Many bonus triggers
                symbolWeights["SYM8"] = 12;  // Minimal low-value symbols
                symbolWeights["SYM9"] = 12;
                symbolWeights["SYM10"] = 12;
            }

            var weightedSymbols = symbolWeights
                .SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value))
                .ToList();

            // Shuffle the weighted symbols
            for (int shuffleIndex = weightedSymbols.Count - 1; shuffleIndex > 0; shuffleIndex--)
            {
                int j = rng.Next(shuffleIndex + 1);
                var temp = weightedSymbols[shuffleIndex];
                weightedSymbols[shuffleIndex] = weightedSymbols[j];
                weightedSymbols[j] = temp;
            }

            // Generate reels with optimized feature distribution
            for (int col = 0; col < 5; col++)
            {
                var strip = new List<string>();
                int scatterCount = 0;
                int wildCount = 0;

                // Determine max features based on RTP tier
                int maxScatters, maxWilds;
                switch (rtpTier)
                {
                    case "UltraLow":
                        maxScatters = 1; maxWilds = 0; // Minimal features
                        break;
                    case "Low":
                        maxScatters = 2; maxWilds = 1; // Few features
                        break;
                    case "Mid":
                        maxScatters = 3; maxWilds = 2; // Balanced features
                        break;
                    case "High":
                        maxScatters = 4; maxWilds = 3; // Many features
                        break;
                    case "UltraHigh":
                        maxScatters = 5; maxWilds = 4; // Feature-rich
                        break;
                    default:
                        maxScatters = 3; maxWilds = 2; // Default balanced
                        break;
                }

                for (int row = 0; row < 20; row++)
                {
                    string chosen;
                    do
                    {
                        chosen = weightedSymbols[rng.Next(weightedSymbols.Count)];
                        if (chosen == "SYM0" && scatterCount >= maxScatters) continue;
                        if (chosen == "SYM1" && wildCount >= maxWilds) continue;
                        break;
                    } while (true);
                    
                    if (chosen == "SYM0") scatterCount++;
                    if (chosen == "SYM1") wildCount++;
                    
                    // Optimized visible area bias for 40% hit rate target
                    if (row < 3 && rng.NextDouble() < 0.20)
                    {
                        if (rtpTier == "UltraLow" || rtpTier == "Low")
                        {
                            // Conservative visible area - avoid too many high-value symbols
                            chosen = new[] { "SYM8", "SYM9", "SYM10", "SYM7", "SYM6" }[rng.Next(5)];
                        }
                        else if (rtpTier == "Mid")
                        {
                            // Balanced visible area - mix of all symbols
                            chosen = new[] { "SYM6", "SYM7", "SYM8", "SYM4", "SYM5", "SYM3" }[rng.Next(6)];
                        }
                        else if (rtpTier == "High" || rtpTier == "UltraHigh")
                        {
                            // Aggressive visible area - more high-value symbols
                            chosen = new[] { "SYM3", "SYM4", "SYM5", "SYM6", "SYM1", "SYM0" }[rng.Next(6)];
                        }
                    }
                    strip.Add(chosen);
                }
                reels.Add(strip);
            }

            return new ReelSet
            {
                Name = $"{tag}Set_{index + 1}",
                Reels = reels
            };
        }

        private async Task<List<ReelSet>> ProcessMonteCarloParallelAsync(List<ReelSet> reelSets, int betInCoins, CancellationToken cancellationToken)
        {
            var processedReelSets = new ConcurrentBag<ReelSet>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(reelSets, new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                }, reelSet =>
                {
                    var mcStart = DateTime.UtcNow;
                    (double rtp, double hitRate) = Shared.ReelSetGenerator.MonteCarloSimulate(
                        reelSet, _config.Paylines, _config.MonteCarloSpins, betInCoins, _config, _config.DefaultLevel);
                    var mcElapsed = DateTime.UtcNow - mcStart;
                    
                    reelSet.ExpectedRtp = rtp;
                    reelSet.EstimatedHitRate = hitRate;
                    
                    processedReelSets.Add(reelSet);
                });
            }, cancellationToken);

            return processedReelSets.ToList();
        }

        private string GetRtpTag(string setName)
        {
            if (setName.StartsWith("UltraHighRtp")) return "UltraHighRtp";
            if (setName.StartsWith("HighRtp")) return "HighRtp";
            if (setName.StartsWith("MidRtp")) return "MidRtp";
            if (setName.StartsWith("LowRtp")) return "LowRtp";
            if (setName.StartsWith("UltraLowRtp")) return "UltraLowRtp";
            return "Unknown";
        }
    }
}
