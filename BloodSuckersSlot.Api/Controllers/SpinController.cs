using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using Shared;
using System.Text.Json;

namespace BloodSuckersSlot.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpinController : ControllerBase
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly ILogger<SpinController> _logger;
        private static GameConfig _config = GameConfig.CreateBalanced(); // TODO: make configurable
        private static List<ReelSet> _allReelSets = new List<ReelSet>();
        private static bool _isLoading = false;
        private static bool _isLoaded = false;
        private static int _totalReelSets = 0;
        private static int _loadedCount = 0;

        public SpinController(IConfiguration configuration, ILogger<SpinController> logger)
        {
            _logger = logger;
            var connectionString = configuration["MongoDb:ConnectionString"];
            var dbName = configuration["MongoDb:Database"];
            var client = new MongoClient(connectionString);
            var db = client.GetDatabase(dbName);
            _collection = db.GetCollection<BsonDocument>("reelsets");
            
            // Start loading reel sets if not already loaded
            if (!_isLoaded && !_isLoading)
            {
                _ = LoadAllReelSetsAsync();
            }
        }

        private async Task LoadAllReelSetsAsync()
        {
            if (_isLoading || _isLoaded) return;
            
            _isLoading = true;
            _loadedCount = 0;
            var startTime = DateTime.UtcNow;
            
            try
            {
                _logger.LogInformation("Starting aggressive multi-threaded parallel batch fetching from MongoDB...");
                
                // Get total count first
                _totalReelSets = (int)await _collection.CountDocumentsAsync(new BsonDocument());
                int batchSize = 10000; // Reduced to 10,000 for more granular progress
                int numThreads = 16; // Increased from Environment.ProcessorCount to 16 threads
                int numBatches = (_totalReelSets + batchSize - 1) / batchSize;
                
                _logger.LogInformation($"Total reel sets: {_totalReelSets:N0}, Batch size: {batchSize:N0}, Number of batches: {numBatches}, Threads: {numThreads}");
                
                var tasks = new List<Task<List<ReelSet>>>();
                var allReelSets = new List<ReelSet>(_totalReelSets);
                var loadedCount = 0;
                var lockObj = new object();
                var completedBatches = 0;
                var lastProgressTime = startTime;
                
                // Limit concurrent operations to avoid overwhelming MongoDB
                var semaphore = new SemaphoreSlim(numThreads);
                
                for (int i = 0; i < numBatches; i++)
                {
                    int skip = i * batchSize;
                    int limit = Math.Min(batchSize, _totalReelSets - skip);
                    int batchIndex = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var batchStartTime = DateTime.UtcNow;
                            _logger.LogInformation($"Starting batch {batchIndex + 1}/{numBatches} (skip: {skip:N0}, limit: {limit:N0}) at {batchStartTime:HH:mm:ss}");
                            
                            var batch = await _collection.Find(new BsonDocument())
                                .Skip(skip)
                                .Limit(limit)
                                .ToListAsync();
                            
                            var batchEndTime = DateTime.UtcNow;
                            var batchDuration = (batchEndTime - batchStartTime).TotalSeconds;
                            _logger.LogInformation($"Batch {batchIndex + 1} fetched {batch.Count:N0} documents in {batchDuration:F2}s at {batchEndTime:HH:mm:ss}");
                            
                            var reelSets = batch.Select(doc => new ReelSet
                            {
                                Name = doc.GetValue("name", "").AsString,
                                Reels = doc["reels"].AsBsonArray
                                    .Select(col => col.AsBsonArray.Select(s => s.AsString).ToList()).ToList(),
                                ExpectedRtp = doc.GetValue("expectedRtp", 0.0).ToDouble(),
                                EstimatedHitRate = doc.GetValue("estimatedHitRate", 0.0).ToDouble()
                            }).ToList();
                            
                            lock (lockObj)
                            {
                                allReelSets.AddRange(reelSets);
                                loadedCount += reelSets.Count;
                                _loadedCount = loadedCount;
                                completedBatches++;
                                
                                var currentTime = DateTime.UtcNow;
                                var elapsed = (currentTime - startTime).TotalSeconds;
                                var progress = (double)loadedCount / _totalReelSets;
                                var estimatedTotalTime = elapsed / progress;
                                var remainingTime = estimatedTotalTime - elapsed;
                                
                                // Calculate rate and time metrics
                                var timeSinceLastProgress = (currentTime - lastProgressTime).TotalSeconds;
                                var setsPerSecond = loadedCount / elapsed;
                                var batchesPerSecond = completedBatches / elapsed;
                                var avgTimePerBatch = elapsed / completedBatches;
                                
                                _logger.LogInformation($"Progress: {loadedCount:N0}/{_totalReelSets:N0} ({progress:P1}) - " +
                                    $"Completed batches: {completedBatches}/{numBatches} - " +
                                    $"Elapsed: {elapsed:F1}s - ETA: {remainingTime:F1}s - " +
                                    $"Rate: {setsPerSecond:F0} sets/sec, {batchesPerSecond:F1} batches/sec - " +
                                    $"Avg time per batch: {avgTimePerBatch:F2}s - " +
                                    $"Current time: {currentTime:HH:mm:ss}");
                                
                                lastProgressTime = currentTime;
                            }
                            return reelSets;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }
                
                _logger.LogInformation($"Launched {tasks.Count} parallel tasks with {numThreads} concurrent threads at {DateTime.UtcNow:HH:mm:ss}. Waiting for completion...");
                await Task.WhenAll(tasks);
                
                var totalTime = (DateTime.UtcNow - startTime).TotalSeconds;
                var finalRate = _allReelSets.Count / totalTime;
                _logger.LogInformation($"All tasks completed at {DateTime.UtcNow:HH:mm:ss}! " +
                    $"Total time: {totalTime:F2}s, Average time per batch: {totalTime / numBatches:F2}s, " +
                    $"Final rate: {finalRate:F0} sets/sec");
                
                _allReelSets = allReelSets;
                _isLoaded = true;
                _isLoading = false;
                
                _logger.LogInformation($"Successfully loaded all {_allReelSets.Count:N0} reel sets into memory in {totalTime:F2} seconds");
            }
            catch (Exception ex)
            {
                var totalTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, $"Error loading reel sets after {totalTime:F2} seconds. Loaded: {_loadedCount:N0}/{_totalReelSets:N0}");
                _isLoading = false;
                _isLoaded = false;
            }
        }

        [HttpGet("loading-status")]
        public IActionResult GetLoadingStatus()
        {
            return Ok(new
            {
                isLoading = _isLoading,
                isLoaded = _isLoaded,
                totalReelSets = _totalReelSets,
                loadedCount = _loadedCount,
                progressPercentage = _totalReelSets > 0 ? (double)_loadedCount / _totalReelSets : 0
            });
        }

        [HttpPost("spin")]
        public async Task<IActionResult> Spin([FromBody] SpinRequestDto request)
        {
            if (!_isLoaded)
            {
                return StatusCode(503, new { error = "Reel sets are still loading. Please wait." });
            }

            if (_allReelSets.Count == 0)
            {
                return StatusCode(500, new { error = "No reel sets available." });
            }

            try
            {
                _logger.LogInformation($"Starting spin with bet amount: {request.BetAmount}");
                
                // Use static helper for spin logic
                var spinResultTuple = SpinLogicHelper.SpinWithReelSets(_config, request.BetAmount, _allReelSets);
                var result = spinResultTuple.Result;
                var grid = spinResultTuple.Grid;
                var chosenSet = spinResultTuple.ChosenSet;
                var winningLines = spinResultTuple.WinningLines;

                if (result == null || grid == null || chosenSet == null)
                {
                    _logger.LogWarning("Spin returned null values - this might be due to no valid reel sets available");
                    return StatusCode(500, new { error = "Spin failed or was delayed." });
                }

                // Get actual RTP and Hit Rate from the helper
                var actualRtp = SpinLogicHelper.GetActualRtp();
                var actualHitRate = SpinLogicHelper.GetActualHitRate();

                _logger.LogInformation($"Spin completed successfully - TotalWin: {result.TotalWin}, RTP: {actualRtp}, HitRate: {actualHitRate}, WinningLines: {winningLines?.Count ?? 0}");

                // Debug winning lines
                if (winningLines != null && winningLines.Count > 0)
                {
                    foreach (var line in winningLines)
                    {
                        _logger.LogInformation($"Winning line: Symbol={line.Symbol}, Count={line.Count}, WinAmount={line.WinAmount}, Positions={line.Positions?.Count ?? 0}, SvgPath={line.SvgPath}");
                    }
                }
                else
                {
                    _logger.LogInformation("No winning lines found");
                }

                // Return grid, win info, RTP/hitrate, and winning lines
                return Ok(new
                {
                    grid,
                    result,
                    chosenReelSet = new {
                        chosenSet.Name,
                        chosenSet.ExpectedRtp,
                        chosenSet.EstimatedHitRate
                    },
                    rtp = actualRtp,
                    hitRate = actualHitRate,
                    winningLines = winningLines ?? new List<BloodSuckersSlot.Api.Models.WinningLine>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during spin");
                return StatusCode(500, new { error = $"Spin failed: {ex.Message}" });
            }
        }
    }

    public class SpinRequestDto
    {
        public int BetAmount { get; set; } = 25;
        // Add more fields as needed (e.g., user/session info)
    }
} 