

namespace BloodSuckersSlot
{
    public class SymbolConfig
    {
        public string SymbolId { get; set; } = "";
        public bool IsWild { get; set; } = false;
        public bool IsScatter { get; set; } = false;
        public bool IsBonus { get; set; } = false;
        public Dictionary<int, double> Payouts { get; set; } = new();
    }
}
