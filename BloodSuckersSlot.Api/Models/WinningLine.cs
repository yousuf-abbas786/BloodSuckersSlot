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
    }

    public class Position
    {
        public int Col { get; set; }
        public int Row { get; set; }
    }
} 