namespace Shared
{
    public class RtpUpdate
    {
        public int SpinNumber { get; set; }
        public double ActualRtp { get; set; }
        public double TargetRtp { get; set; }
        public double ActualHitRate { get; set; }
        public double TargetHitRate { get; set; }
        public DateTime Timestamp { get; set; }
        // Performance metrics
        public double SpinTimeSeconds { get; set; }
        public double AverageSpinTimeSeconds { get; set; }
        public int TotalSpins { get; set; }
        // Reel set selection analysis
        public int HighRtpSetCount { get; set; }
        public int MidRtpSetCount { get; set; }
        public int LowRtpSetCount { get; set; }
        public int SafeFallbackCount { get; set; }
        public string ChosenReelSetName { get; set; }
        public double ChosenReelSetExpectedRtp { get; set; }
        // Monte Carlo performance
        public int MonteCarloSpins { get; set; }
        public int TotalReelSetsGenerated { get; set; }
        public int ReelSetsFiltered { get; set; }
        public double MonteCarloAccuracy { get; set; } // Expected vs Actual RTP difference
        // Game feature stats
        public int TotalFreeSpinsAwarded { get; set; }
        public int TotalBonusesTriggered { get; set; }
    }
} 