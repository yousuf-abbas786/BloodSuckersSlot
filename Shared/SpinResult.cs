namespace Shared
{
    public class SpinResult
    {
        public double TotalWin { get; set; }
        public double ScatterWin { get; set; }
        public double LineWin { get; set; }
        public double WildWin { get; set; }
        public double BonusWin { get; set; }
        public int ScatterCount { get; set; }
        public string BonusLog { get; set; } = "";
        public bool IsFreeSpin { get; set; }
        public bool BonusTriggered { get; set; }
        
        // FIXED: Add free spin and bonus tracking
        public int FreeSpinsRemaining { get; set; }
        public int FreeSpinsAwarded { get; set; }
        public int TotalFreeSpinsAwarded { get; set; }
        public int TotalBonusesTriggered { get; set; }
        public string SpinType { get; set; } = ""; // "PAID SPIN" or "FREE SPIN"
    }
} 