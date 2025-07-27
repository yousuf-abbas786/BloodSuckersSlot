using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Text.Json;

namespace BloodSuckersSlot.Api.Controllers
{
    // DTO for stats response
    public class ReelSetStatsDto
    {
        public int TotalCount { get; set; }
        public Dictionary<string, int> TagCounts { get; set; } = new();
        public double AvgRtp { get; set; }
        public double MinRtp { get; set; }
        public double MaxRtp { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class ReelSetsController : ControllerBase
    {
        private readonly IMongoCollection<BsonDocument>? _collection;
        private readonly ILogger<ReelSetsController> _logger;

        public ReelSetsController(IConfiguration configuration, ILogger<ReelSetsController> logger)
        {
            _logger = logger;
            try
            {
                var connectionString = configuration["MongoDb:ConnectionString"];
                var dbName = configuration["MongoDb:Database"];
                
                _logger.LogInformation($"Connecting to MongoDB: {connectionString}");
                _logger.LogInformation($"Database: {dbName}");
                
                var client = new MongoClient(connectionString);
                var db = client.GetDatabase(dbName);
                _collection = db.GetCollection<BsonDocument>("reelsets");
                
                _logger.LogInformation("MongoDB connection established successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize MongoDB connection");
                _collection = null; // Don't throw, just set to null
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetReelSets(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? tag = null,
            [FromQuery] double? minRtp = null,
            [FromQuery] double? maxRtp = null,
            [FromQuery] string? searchTerm = null)
        {
            if (_collection == null)
            {
                _logger.LogError("MongoDB collection is null - connection failed");
                return StatusCode(500, new { error = "Database connection not available" });
            }

            try
            {
                var filter = Builders<BsonDocument>.Filter.Empty;

                // Apply filters
                if (!string.IsNullOrEmpty(tag))
                {
                    filter &= Builders<BsonDocument>.Filter.Eq("tag", tag);
                }

                if (minRtp.HasValue)
                {
                    filter &= Builders<BsonDocument>.Filter.Gte("expectedRtp", minRtp.Value);
                }

                if (maxRtp.HasValue)
                {
                    filter &= Builders<BsonDocument>.Filter.Lte("expectedRtp", maxRtp.Value);
                }

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    filter &= Builders<BsonDocument>.Filter.Regex("name", new MongoDB.Bson.BsonRegularExpression(searchTerm, "i"));
                }

                // Get total count
                var totalCount = await _collection.CountDocumentsAsync(filter);

                // Calculate pagination
                var skip = (pageNumber - 1) * pageSize;
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

                // Get paginated results
                var documents = await _collection
                    .Find(filter)
                    .Sort(Builders<BsonDocument>.Sort.Descending("expectedRtp"))
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync();

                var items = documents.Select(doc => new
                {
                    Id = doc["_id"].AsObjectId.ToString(),
                    Name = doc["name"].AsString,
                    Tag = doc["tag"].AsString,
                    ExpectedRtp = doc["expectedRtp"].AsDouble,
                    EstimatedHitRate = doc["estimatedHitRate"].AsDouble,
                    CreatedAt = doc.Contains("createdAt") ? doc["createdAt"].ToUniversalTime() : DateTime.UtcNow
                }).ToList();

                var result = new
                {
                    Items = items,
                    TotalCount = (int)totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalPages = totalPages
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reel sets");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetReelSetDetail(string id)
        {
            if (_collection == null)
            {
                _logger.LogError("MongoDB collection is null - connection failed");
                return StatusCode(500, new { error = "Database connection not available" });
            }

            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                    return BadRequest(new { error = "Invalid ID format" });

                var filter = Builders<BsonDocument>.Filter.Eq("_id", objectId);
                var document = await _collection.Find(filter).FirstOrDefaultAsync();

                if (document == null)
                    return NotFound(new { error = "Reel set not found" });

                var reels = new List<List<string>>();
                if (document.Contains("reels") && document["reels"].IsBsonArray)
                {
                    var reelsArray = document["reels"].AsBsonArray;
                    foreach (var reel in reelsArray)
                    {
                        var reelList = reel.AsBsonArray.Select(symbol => symbol.AsString).ToList();
                        reels.Add(reelList);
                    }
                }

                var result = new
                {
                    Id = document["_id"].AsObjectId.ToString(),
                    Name = document["name"].AsString,
                    Tag = document["tag"].AsString,
                    ExpectedRtp = document["expectedRtp"].AsDouble,
                    EstimatedHitRate = document["estimatedHitRate"].AsDouble,
                    Reels = reels,
                    CreatedAt = document.Contains("createdAt") ? document["createdAt"].ToUniversalTime() : DateTime.UtcNow
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reel set detail");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("tags")]
        public async Task<IActionResult> GetAvailableTags()
        {
            if (_collection == null)
            {
                _logger.LogError("MongoDB collection is null - connection failed");
                return StatusCode(500, new { error = "Database connection not available" });
            }

            try
            {
                var tags = await _collection.DistinctAsync<string>("tag", Builders<BsonDocument>.Filter.Empty);
                return Ok(tags.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available tags");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            if (_collection == null)
            {
                _logger.LogError("MongoDB collection is null - connection failed");
                return StatusCode(500, new { error = "Database connection not available" });
            }

            try
            {
                _logger.LogInformation("Starting GetStats");
                var stats = new ReelSetStatsDto();

                // Total count
                _logger.LogInformation("Getting total count");
                var totalCount = await _collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                stats.TotalCount = (int)totalCount;
                _logger.LogInformation($"Total count: {totalCount}");

                // Count by tag
                _logger.LogInformation("Getting tag counts");
                try
                {
                    var tagCounts = await _collection.AggregateAsync<BsonDocument>(new[]
                    {
                        new BsonDocument("$group", new BsonDocument
                        {
                            { "_id", "$tag" },
                            { "count", new BsonDocument("$sum", 1) }
                        })
                    });
                    var tagStats = await tagCounts.ToListAsync();
                    stats.TagCounts = tagStats
                        .Where(x => x.Contains("_id") && x["_id"] != null && x.Contains("count") && x["count"] != null)
                        .ToDictionary(
                            x => x["_id"].IsBsonNull ? "" : x["_id"].AsString,
                            x => x["count"].IsBsonNull ? 0 : x["count"].AsInt32
                        );
                    _logger.LogInformation($"Tag counts: {string.Join(", ", tagStats.Select(x => $"{x["_id"]}: {x["count"]}"))}");
                }
                catch (Exception tagEx)
                {
                    _logger.LogError(tagEx, "Error getting tag counts, using empty dictionary");
                    stats.TagCounts = new Dictionary<string, int>();
                }

                // RTP statistics
                _logger.LogInformation("Getting RTP statistics");
                try
                {
                    var rtpStats = await _collection.AggregateAsync<BsonDocument>(new[]
                    {
                        new BsonDocument("$match", new BsonDocument {
                            { "expectedRtp", new BsonDocument { { "$exists", true }, { "$ne", BsonNull.Value }, { "$type", "number" } } }
                        }),
                        new BsonDocument("$group", new BsonDocument
                        {
                            { "_id", BsonNull.Value },
                            { "avgRtp", new BsonDocument("$avg", "$expectedRtp") },
                            { "minRtp", new BsonDocument("$min", "$expectedRtp") },
                            { "maxRtp", new BsonDocument("$max", "$expectedRtp") }
                        })
                    });
                    var rtpResult = await rtpStats.FirstOrDefaultAsync();
                    if (rtpResult != null)
                    {
                        stats.AvgRtp = rtpResult.Contains("avgRtp") && !rtpResult["avgRtp"].IsBsonNull ? rtpResult["avgRtp"].ToDouble() : 0.0;
                        stats.MinRtp = rtpResult.Contains("minRtp") && !rtpResult["minRtp"].IsBsonNull ? rtpResult["minRtp"].ToDouble() : 0.0;
                        stats.MaxRtp = rtpResult.Contains("maxRtp") && !rtpResult["maxRtp"].IsBsonNull ? rtpResult["maxRtp"].ToDouble() : 0.0;
                        _logger.LogInformation($"RTP stats - Avg: {stats.AvgRtp}, Min: {stats.MinRtp}, Max: {stats.MaxRtp}");
                    }
                    else
                    {
                        _logger.LogWarning("No RTP statistics found");
                        stats.AvgRtp = 0.0;
                        stats.MinRtp = 0.0;
                        stats.MaxRtp = 0.0;
                    }
                }
                catch (Exception rtpEx)
                {
                    _logger.LogError(rtpEx, "Error getting RTP statistics, using defaults");
                    stats.AvgRtp = 0.0;
                    stats.MinRtp = 0.0;
                    stats.MaxRtp = 0.0;
                }

                _logger.LogInformation("GetStats completed successfully");
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetStats: {Message}", ex.Message);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }
    }
} 