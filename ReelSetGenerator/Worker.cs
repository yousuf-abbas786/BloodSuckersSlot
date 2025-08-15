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
        private DateTime _lastThrottleCheck = DateTime.UtcNow;
        private int _currentBatchDelay = 2000; // Dynamic delay between batches


        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            var connectionString = _configuration["MongoDb:ConnectionString"];
            var dbName = _configuration["MongoDb:Database"];
            var client = new MongoClient(connectionString);
            var db = client.GetDatabase(dbName);
            _collection = db.GetCollection<BsonDocument>("reelsets1");

            // Ensure indexes exist for optimal performance
            var indexKeys = new List<CreateIndexModel<BsonDocument>>
            {
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("name")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("tag")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("expectedRtp")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("estimatedHitRate")),
                // Compound index for efficient querying
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Combine(
                    Builders<BsonDocument>.IndexKeys.Ascending("tag"),
                    Builders<BsonDocument>.IndexKeys.Ascending("expectedRtp")
                ))
            };
            _collection.Indexes.CreateMany(indexKeys);
            
            // Validate collection is empty before starting (will be done in ExecuteAsync)
            _logger.LogInformation("Collection validation will be performed at startup...");
            
            // Load GameConfig from appsettings
            _config = GameConfigLoader.LoadFromConfiguration(_configuration);
            

            
            // Use only 1 CPU core for sequential processing
            _maxDegreeOfParallelism = 1;
            
            _logger.LogInformation("Using adaptive throttling to manage server load");
            
            _logger.LogInformation($"Loaded GameConfig from appsettings - RTP Target: {_config.RtpTarget:P1}, Hit Rate Target: {_config.TargetHitRate:P1}");
            _logger.LogInformation("Simple single instance configuration loaded");
            _logger.LogInformation($"Using sequential processing (1 thread) to minimize CPU usage");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Check existing progress and allow resumption
            var existingCount = await _collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            if (existingCount > 0)
            {
                _logger.LogWarning($"Collection 'reelsets1' already contains {existingCount:N0} documents.");
                _logger.LogInformation($"Generation can resume from where it left off.");
            }
            
            int totalReelSets = 100000; // 100K reel sets per app instance
            int batchSize = 100; // Smaller batch size for faster progress feedback
            int processed = (int)existingCount; // Start from where we left off
            int ultraHighRtpCount = 0, highRtpCount = 0, midRtpCount = 0, lowRtpCount = 0, ultraLowRtpCount = 0;
            var startTime = DateTime.UtcNow;
            var betInCoins = BettingSystem.CalculateBetInCoins(_config.BaseBetPerLevel, _config.DefaultLevel);

            _logger.LogInformation($"Starting generation of {totalReelSets:N0} reel sets with {_maxDegreeOfParallelism} parallel threads");
            
            // Check if we've already generated enough reel sets
            if (processed >= totalReelSets)
            {
                _logger.LogInformation($"Generation already complete! {processed:N0}/{totalReelSets:N0} reel sets exist.");
                return;
            }

            // Process in batches for memory efficiency - skip already processed reel sets
            for (int batchStart = processed; batchStart < totalReelSets; batchStart += batchSize)
            {
                if (stoppingToken.IsCancellationRequested) break;

                int currentBatchSize = Math.Min(batchSize, totalReelSets - batchStart);
                var batchNumber = batchStart / batchSize + 1;
                var totalBatches = (totalReelSets + batchSize - 1) / batchSize;
                
                // Simple start index - no offset complexity
                var globalStartIndex = batchStart;
                _logger.LogInformation($"=== BATCH {batchNumber}/{totalBatches} === Generating {currentBatchSize:N0} reel sets (Global IDs {globalStartIndex + 1} to {globalStartIndex + currentBatchSize})");

                // Generate reel sets in parallel (quick step)
                var reelSets = await GenerateReelSetsParallelAsync(currentBatchSize, globalStartIndex, stoppingToken);
                _logger.LogInformation($"Generated {reelSets.Count} reel set structures. Starting Monte Carlo simulations (500K spins each)...");

                // Process Monte Carlo simulations sequentially
                var batchMcStart = DateTime.UtcNow;
                var processedReelSets = await ProcessMonteCarloSequentialAsync(reelSets, betInCoins, stoppingToken);
                var batchMcElapsed = DateTime.UtcNow - batchMcStart;
                
                _logger.LogInformation($"Batch {batchNumber} Monte Carlo completed in {batchMcElapsed.TotalMinutes:F1} minutes ({batchMcElapsed.TotalSeconds / reelSets.Count:F1}s per reel set)");

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

                // Time the database operation to identify bottlenecks
                _logger.LogWarning($"🔄 DATABASE INSERT STARTING: {documents.Count} documents...");
                var dbStart = DateTime.UtcNow;
                await _collection.InsertManyAsync(documents, cancellationToken: stoppingToken);
                var dbElapsed = DateTime.UtcNow - dbStart;
                
                processed += processedReelSets.Count;
                
                // Make database timing VERY visible
                if (dbElapsed.TotalSeconds > 2.0)
                {
                    _logger.LogError($"🐌 DATABASE SLOW: Insert took {dbElapsed.TotalSeconds:F1}s for {documents.Count} documents (>{dbElapsed.TotalSeconds/documents.Count:F2}s per document)");
                }
                else
                {
                    _logger.LogInformation($"✅ DATABASE OK: Insert took {dbElapsed.TotalSeconds:F1}s for {documents.Count} documents");
                }

                // Adaptive throttling based on processing time  
                await AdaptiveThrottling(processedReelSets.Count, stoppingToken);
                
                // Additional delay if database is slow/busy
                await Task.Delay(1000, stoppingToken); // Extra 1 second for database breathing room

                var elapsed = DateTime.UtcNow - startTime;
                
                // Log progress with ETA
                var avgTimePerBatch = elapsed.TotalMilliseconds / (batchStart / batchSize + 1);
                var remainingBatches = (totalReelSets - batchStart - currentBatchSize) / batchSize;
                var eta = TimeSpan.FromMilliseconds(avgTimePerBatch * remainingBatches);
                
                var dbPerformanceIcon = dbElapsed.TotalSeconds > 2.0 ? "🐌" : "✅";
                _logger.LogInformation($"Progress: {processed:N0}/{totalReelSets:N0} ({processed * 100.0 / totalReelSets:F1}%) - ETA: {eta:hh\\:mm\\:ss} | DB: {dbPerformanceIcon} {dbElapsed.TotalSeconds:F1}s");
                _logger.LogInformation($"Distribution: UltraHigh: {ultraHighRtpCount}, High: {highRtpCount}, Mid: {midRtpCount}, Low: {lowRtpCount}, UltraLow: {ultraLowRtpCount}");
                var rate = processed / elapsed.TotalSeconds;
                var finalEta = TimeSpan.FromSeconds((totalReelSets - processed) / rate);

                _logger.LogInformation($"Processed {processed:N0}/{totalReelSets:N0} reel sets " +
                                    $"(Rate: {rate:F0}/sec, Elapsed: {elapsed.TotalMinutes:F1}min, ETA: {eta.TotalMinutes:F1}min)");
            }

            var totalElapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation($"Reel set generation complete in {totalElapsed.TotalMinutes:F1} minutes");
            _logger.LogInformation($"Final counts - UltraHighRtp: {ultraHighRtpCount:N0}, HighRtp: {highRtpCount:N0}, MidRtp: {midRtpCount:N0}, LowRtp: {lowRtpCount:N0}, UltraLowRtp: {ultraLowRtpCount:N0}");
            
            // Final validation
            await ValidateFinalCollection();
        }

        private async Task<List<ReelSet>> GenerateReelSetsParallelAsync(int count, int startIndex, CancellationToken cancellationToken)
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
                    var absoluteIndex = startIndex + i;
                    var reelSet = GenerateSingleReelSetOptimized(absoluteIndex);
                    reelSets.Add(reelSet);
                });
            }, cancellationToken);

            return reelSets.ToList();
        }

        // Old GenerateSingleReelSet method removed - using optimized version that handles instance offsets correctly

        private Task<List<ReelSet>> ProcessMonteCarloSequentialAsync(List<ReelSet> reelSets, int betInCoins, CancellationToken cancellationToken)
        {
            var processedReelSets = new List<ReelSet>();
            
            int index = 0;
            foreach (var reelSet in reelSets)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                _logger.LogInformation($"Processing reel set {index + 1}/{reelSets.Count}: {reelSet.Name}");
                
                var mcStart = DateTime.UtcNow;
                (double rtp, double hitRate) = Shared.ReelSetGenerator.MonteCarloSimulate(
                    reelSet, _config.Paylines, _config.MonteCarloSpins, betInCoins, _config, _config.DefaultLevel);
                var mcElapsed = DateTime.UtcNow - mcStart;
                
                reelSet.ExpectedRtp = rtp;
                reelSet.EstimatedHitRate = hitRate;
                
                processedReelSets.Add(reelSet);
                
                _logger.LogInformation($"Completed {reelSet.Name}: RTP={rtp:P2}, HitRate={hitRate:P2}, Time={mcElapsed.TotalSeconds:F1}s");
                
                index++;
            }

            return Task.FromResult(processedReelSets);
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
        
        private async Task ValidateFinalCollection()
        {
            _logger.LogInformation("Validating final collection...");
            
            var totalCount = await _collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            _logger.LogInformation($"Total documents in reelsets1: {totalCount:N0}");
            
            // Count by tag
            var pipeline = new BsonDocument[]
            {
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$tag" },
                    { "count", new BsonDocument("$sum", 1) },
                    { "avgRtp", new BsonDocument("$avg", "$expectedRtp") },
                    { "avgHitRate", new BsonDocument("$avg", "$estimatedHitRate") }
                })
            };
            
            var results = await _collection.AggregateAsync<BsonDocument>(pipeline);
            var resultsList = await results.ToListAsync();
            
            foreach (var result in resultsList)
            {
                var tag = result["_id"].AsString;
                var count = result["count"].AsInt32;
                var avgRtp = result["avgRtp"].AsDouble;
                var avgHitRate = result["avgHitRate"].AsDouble;
                
                _logger.LogInformation($"Tag: {tag} - Count: {count:N0} - Avg RTP: {avgRtp:P2} - Avg Hit Rate: {avgHitRate:P2}");
            }
            
            _logger.LogInformation("✅ Collection validation complete. Ready for migration!");
        }

        private async Task AdaptiveThrottling(int reelSetsProcessed, CancellationToken stoppingToken)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastCheck = (now - _lastThrottleCheck).TotalSeconds;
            _lastThrottleCheck = now;

            // Calculate processing rate (reel sets per second)
            double processingRate = reelSetsProcessed / Math.Max(timeSinceLastCheck, 1);

            // Adaptive delay based on processing intensity
            // For 500K simulations per reel set, we expect lower processing rates
            if (processingRate > 2.0) // Very fast processing
            {
                _currentBatchDelay = 1000; // 1 second delay
            }
            else if (processingRate > 1.0) // Normal processing
            {
                _currentBatchDelay = 2000; // 2 second delay
            }
            else if (processingRate > 0.5) // Slow processing (expected with 500K simulations)
            {
                _currentBatchDelay = 3000; // 3 second delay
            }
            else // Very slow processing
            {
                _currentBatchDelay = 5000; // 5 second delay - gives server more breathing room
            }

            _logger.LogDebug($"Processing rate: {processingRate:F2} reel sets/sec, Using {_currentBatchDelay}ms delay");
            
            if (_currentBatchDelay > 0)
            {
                await Task.Delay(_currentBatchDelay, stoppingToken);
            }
        }

        private ReelSet GenerateSingleReelSetOptimized(int index)
        {

            
            // Use thread-local random for better performance in parallel processing
            var rng = new Random(Guid.NewGuid().GetHashCode() ^ Thread.CurrentThread.ManagedThreadId);
            var reels = new List<List<string>>(5); // Pre-allocate with capacity
            
            // Pre-allocate reel strips
            for (int i = 0; i < 5; i++)
            {
                reels.Add(new List<string>(20)); // Pre-allocate with expected capacity
            }

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
            
            // Simple index calculation
            int relativeIndex = index;
            
            // 5-tier distribution for 100K reel sets per instance (88% RTP, 40% hit rate)
            if (relativeIndex < 100000 * 0.15)  // 0-14,999
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
            else if (relativeIndex < 100000 * 0.35)  // 15,000-34,999
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
            else if (relativeIndex < 100000 * 0.65)  // 35,000-64,999
            {
                tag = "MidRtp";
                rtpTier = "Mid";
                // Purpose: Core balanced sets (87-89% RTP, 40-42% hit rate) - TARGET RANGE
                // Use base weights - this is the sweet spot
                // No adjustments needed - base weights are optimized for this range
            }
            else if (relativeIndex < 100000 * 0.85)  // 65,000-84,999
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
            else  // 85,000-99,999
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
                .ToArray(); // Use array for better performance

            // Shuffle the weighted symbols using Fisher-Yates algorithm
            for (int shuffleIndex = weightedSymbols.Length - 1; shuffleIndex > 0; shuffleIndex--)
            {
                int j = rng.Next(shuffleIndex + 1);
                (weightedSymbols[shuffleIndex], weightedSymbols[j]) = (weightedSymbols[j], weightedSymbols[shuffleIndex]);
            }

            // Generate reels with optimized feature distribution
            for (int col = 0; col < 5; col++)
            {
                var strip = reels[col];
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
                    int attempts = 0;
                    do
                    {
                        chosen = weightedSymbols[rng.Next(weightedSymbols.Length)];
                        if (chosen == "SYM0" && scatterCount >= maxScatters) 
                        {
                            attempts++;
                            if (attempts > 10) break; // Prevent infinite loop
                            continue;
                        }
                        if (chosen == "SYM1" && wildCount >= maxWilds) 
                        {
                            attempts++;
                            if (attempts > 10) break; // Prevent infinite loop
                            continue;
                        }
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
            }

            var finalName = $"{tag}Set_{index + 1}";
            

            
            return new ReelSet
            {
                Name = finalName,
                Reels = reels
            };
        }
    }
}
