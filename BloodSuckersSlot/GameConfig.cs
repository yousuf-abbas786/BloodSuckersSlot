

namespace BloodSuckersSlot
{
    public class GameConfig
    {
        public double RtpTarget { get; set; } = 0.88; // Default target RTP (88%)
        public double TargetHitRate { get; set; } = 0.35; // Target hit rate (35%) - adjustable
        public int BaseBetForFreeSpins { get; set; } = 25; // Used when free spins don’t cost a bet
        public List<int[]> Paylines { get; set; } = new(); // List of 5-column payline patterns
        public double MaxRtpPerSet { get; set; } = 1.3;

        public Dictionary<string, SymbolConfig> Symbols { get; set; }

    }

}
