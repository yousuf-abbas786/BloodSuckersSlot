namespace Shared
{
    public class SymbolConfig
    {
        public string SymbolId { get; set; } = "";
        public bool IsWild { get; set; } = false;
        public bool IsScatter { get; set; } = false;
        public bool IsBonus { get; set; } = false;
        public Dictionary<int, double> Payouts { get; set; } = new();
    }

    // Position and WinningLine models moved to Shared for use by evaluation service
    public class Position
    {
        public int Col { get; set; }
        public int Row { get; set; }
    }

    public class WinningLine
    {
        public List<Position> Positions { get; set; } = new();
        public string Symbol { get; set; } = "";
        public int Count { get; set; }
        public double WinAmount { get; set; }
        public string PaylineType { get; set; } = ""; // "line", "wild", "scatter"
        public string SvgPath { get; set; } = "";
        public int PaylineIndex { get; set; } = -1; // Index of the payline that produced this win
        public List<Position> FullPaylinePath { get; set; } = new(); // Full payline path (all 5 positions)
    }
} 