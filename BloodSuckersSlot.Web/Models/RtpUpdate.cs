using System;
using System.Text.Json.Serialization;

namespace BloodSuckersSlot.Web.Models
{
    public class RtpUpdate
    {
        [JsonPropertyName("spinNumber")]
        public int SpinNumber { get; set; }
        
        [JsonPropertyName("actualRtp")]
        public double ActualRtp { get; set; }
        
        [JsonPropertyName("targetRtp")]
        public double TargetRtp { get; set; }
        
        [JsonPropertyName("actualHitRate")]
        public double ActualHitRate { get; set; }
        
        [JsonPropertyName("targetHitRate")]
        public double TargetHitRate { get; set; }
        
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
        
        // Performance metrics
        [JsonPropertyName("spinTimeSeconds")]
        public double SpinTimeSeconds { get; set; }
        
        [JsonPropertyName("averageSpinTimeSeconds")]
        public double AverageSpinTimeSeconds { get; set; }
        
        [JsonPropertyName("totalSpins")]
        public int TotalSpins { get; set; }
        
        // Reel set selection analysis
        [JsonPropertyName("highRtpSetCount")]
        public int HighRtpSetCount { get; set; }
        
        [JsonPropertyName("midRtpSetCount")]
        public int MidRtpSetCount { get; set; }
        
        [JsonPropertyName("lowRtpSetCount")]
        public int LowRtpSetCount { get; set; }
        
        [JsonPropertyName("safeFallbackCount")]
        public int SafeFallbackCount { get; set; }
        
        [JsonPropertyName("chosenReelSetName")]
        public string ChosenReelSetName { get; set; } = "";
        
        [JsonPropertyName("chosenReelSetExpectedRtp")]
        public double ChosenReelSetExpectedRtp { get; set; }
        
        // Monte Carlo performance
        [JsonPropertyName("monteCarloSpins")]
        public int MonteCarloSpins { get; set; }
        
        [JsonPropertyName("totalReelSetsGenerated")]
        public int TotalReelSetsGenerated { get; set; }
        
        [JsonPropertyName("reelSetsFiltered")]
        public int ReelSetsFiltered { get; set; }
        
        [JsonPropertyName("monteCarloAccuracy")]
        public double MonteCarloAccuracy { get; set; } // Expected vs Actual RTP difference
        
        // Game feature stats
        [JsonPropertyName("totalFreeSpinsAwarded")]
        public int TotalFreeSpinsAwarded { get; set; }
        
        [JsonPropertyName("totalBonusesTriggered")]
        public int TotalBonusesTriggered { get; set; }
    }
} 