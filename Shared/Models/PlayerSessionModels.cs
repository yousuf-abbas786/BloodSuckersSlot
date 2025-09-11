using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Models
{
    /// <summary>
    /// Player session tracking for individual gaming sessions
    /// </summary>
    [BsonIgnoreExtraElements]
    public class PlayerSession
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        
        [BsonElement("playerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string PlayerId { get; set; } = string.Empty;
        
        [BsonElement("username")]
        public string Username { get; set; } = string.Empty;
        
        [BsonElement("sessionStart")]
        public DateTime SessionStart { get; set; } = DateTime.UtcNow;
        
        [BsonElement("sessionEnd")]
        public DateTime? SessionEnd { get; set; }
        
        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;
        
        [BsonElement("lastActivity")]
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        
        // Session Statistics
        [BsonElement("totalSpins")]
        public int TotalSpins { get; set; } = 0;
        
        [BsonElement("totalBet")]
        public decimal TotalBet { get; set; } = 0;
        
        [BsonElement("totalWin")]
        public decimal TotalWin { get; set; } = 0;
        
        [BsonElement("totalRtp")]
        public double TotalRtp { get; set; } = 0;
        
        [BsonElement("hitRate")]
        public double HitRate { get; set; } = 0;
        
        [BsonElement("winningSpins")]
        public int WinningSpins { get; set; } = 0;
        
        [BsonElement("freeSpinsAwarded")]
        public int FreeSpinsAwarded { get; set; } = 0;
        
        [BsonElement("bonusesTriggered")]
        public int BonusesTriggered { get; set; } = 0;
        
        [BsonElement("maxWin")]
        public decimal MaxWin { get; set; } = 0;
        
        [BsonElement("currentBalance")]
        public decimal CurrentBalance { get; set; } = 0;
        
        [BsonElement("sessionDuration")]
        public TimeSpan? SessionDuration { get; set; }
        
        [BsonIgnore]
        public TimeSpan? CalculatedSessionDuration => SessionEnd?.Subtract(SessionStart);
        
        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Player statistics aggregated across all sessions
    /// </summary>
    [BsonIgnoreExtraElements]
    public class PlayerStats
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        
        [BsonElement("playerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string PlayerId { get; set; } = string.Empty;
        
        [BsonElement("username")]
        public string Username { get; set; } = string.Empty;
        
        // Lifetime Statistics
        [BsonElement("totalSessions")]
        public int TotalSessions { get; set; } = 0;
        
        [BsonElement("totalSpins")]
        public long TotalSpins { get; set; } = 0;
        
        [BsonElement("totalBet")]
        public decimal TotalBet { get; set; } = 0;
        
        [BsonElement("totalWin")]
        public decimal TotalWin { get; set; } = 0;
        
        [BsonElement("lifetimeRtp")]
        public double LifetimeRtp { get; set; } = 0;
        
        [BsonElement("lifetimeHitRate")]
        public double LifetimeHitRate { get; set; } = 0;
        
        [BsonElement("totalWinningSpins")]
        public long TotalWinningSpins { get; set; } = 0;
        
        [BsonElement("totalFreeSpinsAwarded")]
        public long TotalFreeSpinsAwarded { get; set; } = 0;
        
        [BsonElement("totalBonusesTriggered")]
        public long TotalBonusesTriggered { get; set; } = 0;
        
        [BsonElement("maxWinEver")]
        public decimal MaxWinEver { get; set; } = 0;
        
        [BsonElement("currentBalance")]
        public decimal CurrentBalance { get; set; } = 0;
        
        [BsonElement("firstSessionDate")]
        public DateTime? FirstSessionDate { get; set; }
        
        [BsonElement("lastSessionDate")]
        public DateTime? LastSessionDate { get; set; }
        
        [BsonElement("lastLoginDate")]
        public DateTime? LastLoginDate { get; set; }
        
        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Request DTO for starting a new player session
    /// </summary>
    public class StartSessionRequest
    {
        [Required]
        public string PlayerId { get; set; } = string.Empty;
        
        [Required]
        public string Username { get; set; } = string.Empty;
        
        public decimal InitialBalance { get; set; } = 0;
    }

    /// <summary>
    /// Request DTO for updating session stats
    /// </summary>
    public class UpdateSessionStatsRequest
    {
        [Required]
        public string SessionId { get; set; } = string.Empty;
        
        [Required]
        public string PlayerId { get; set; } = string.Empty;
        
        public decimal BetAmount { get; set; }
        public decimal WinAmount { get; set; }
        public bool IsWinningSpin { get; set; }
        public bool IsFreeSpin { get; set; } = false;
        public bool IsBonusTriggered { get; set; } = false;
        public int FreeSpinsAwarded { get; set; } = 0;
        public decimal CurrentBalance { get; set; }
    }

    /// <summary>
    /// Response DTO for player session information
    /// </summary>
    public class PlayerSessionResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime SessionStart { get; set; }
        public DateTime? SessionEnd { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastActivity { get; set; }
        
        // Session Statistics
        public int TotalSpins { get; set; }
        public decimal TotalBet { get; set; }
        public decimal TotalWin { get; set; }
        public double TotalRtp { get; set; }
        public double HitRate { get; set; }
        public int WinningSpins { get; set; }
        public int FreeSpinsAwarded { get; set; }
        public int BonusesTriggered { get; set; }
        public decimal MaxWin { get; set; }
        public decimal CurrentBalance { get; set; }
        public TimeSpan? SessionDuration { get; set; }
    }

    /// <summary>
    /// Response DTO for player lifetime statistics
    /// </summary>
    public class PlayerStatsResponse
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        
        // Lifetime Statistics
        public int TotalSessions { get; set; }
        public long TotalSpins { get; set; }
        public decimal TotalBet { get; set; }
        public decimal TotalWin { get; set; }
        public double LifetimeRtp { get; set; }
        public double LifetimeHitRate { get; set; }
        public long TotalWinningSpins { get; set; }
        public long TotalFreeSpinsAwarded { get; set; }
        public long TotalBonusesTriggered { get; set; }
        public decimal MaxWinEver { get; set; }
        public decimal CurrentBalance { get; set; }
        public DateTime? FirstSessionDate { get; set; }
        public DateTime? LastSessionDate { get; set; }
        public DateTime? LastLoginDate { get; set; }
    }
}
