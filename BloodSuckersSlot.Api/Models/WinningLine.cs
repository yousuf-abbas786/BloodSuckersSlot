namespace BloodSuckersSlot.Api.Models
{
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

    public class Position
    {
        public int Col { get; set; }
        public int Row { get; set; }
    }
} 