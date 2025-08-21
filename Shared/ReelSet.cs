namespace Shared
{
    public class ReelSet
    {
        public string Name { get; set; }
        public List<List<string>> Reels { get; set; }
        public double ExpectedRtp { get; set; }
        public double EstimatedHitRate { get; set; }
        public double RtpWeight { get; set; }
        public double HitWeight { get; set; }
        public double CombinedWeight { get; set; } // Combined weight for intelligent selection
    }
} 