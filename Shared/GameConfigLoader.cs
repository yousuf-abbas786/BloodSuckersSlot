using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace Shared
{
    public static class GameConfigLoader
    {
        public static GameConfig LoadFromConfiguration(IConfiguration configuration)
        {
            var config = new GameConfig();
            
            // Load basic properties
            config.RtpTarget = configuration.GetValue<double>("GameConfig:RtpTarget", 0.88);
            config.TargetHitRate = configuration.GetValue<double>("GameConfig:TargetHitRate", 0.45);
            config.BaseBetForFreeSpins = configuration.GetValue<int>("GameConfig:BaseBetForFreeSpins", 25);
            
            // Load betting system properties
            configuration.Bind("GameConfig:MaxFreeSpinsPerSession", config.MaxFreeSpinsPerSession);
            configuration.Bind("GameConfig:BaseBetPerLevel", config.BaseBetPerLevel);
            configuration.Bind("GameConfig:DefaultLevel", config.DefaultLevel);
            configuration.Bind("GameConfig:MaxLevel", config.MaxLevel);
            configuration.Bind("GameConfig:DefaultCoinValue", config.DefaultCoinValue);
            configuration.Bind("GameConfig:MinCoinValue", config.MinCoinValue);
            configuration.Bind("GameConfig:MaxCoinValue", config.MaxCoinValue);
            
            // Load paylines
            var paylinesSection = configuration.GetSection("GameConfig:Paylines");
            if (paylinesSection.Exists())
            {
                config.Paylines = new List<int[]>();
                foreach (var payline in paylinesSection.GetChildren())
                {
                    var paylineArray = payline.Get<int[]>();
                    if (paylineArray != null && paylineArray.Length == 5)
                    {
                        config.Paylines.Add(paylineArray);
                    }
                }
            }
            
            // Load symbols
            var symbolsSection = configuration.GetSection("GameConfig:Symbols");
            if (symbolsSection.Exists())
            {
                config.Symbols = new Dictionary<string, SymbolConfig>();
                foreach (var symbolSection in symbolsSection.GetChildren())
                {
                    var symbolId = symbolSection.Key;
                    var symbolConfig = new SymbolConfig
                    {
                        SymbolId = symbolId,
                        IsScatter = symbolSection.GetValue<bool>("IsScatter", false),
                        IsWild = symbolSection.GetValue<bool>("IsWild", false),
                        IsBonus = symbolSection.GetValue<bool>("IsBonus", false),
                        Payouts = new Dictionary<int, double>()
                    };
                    
                    // Load payouts
                    var payoutsSection = symbolSection.GetSection("Payouts");
                    if (payoutsSection.Exists())
                    {
                        foreach (var payout in payoutsSection.GetChildren())
                        {
                            if (int.TryParse(payout.Key, out int count) && 
                                double.TryParse(payout.Value, out double amount))
                            {
                                symbolConfig.Payouts[count] = amount;
                            }
                        }
                    }
                    
                    config.Symbols[symbolId] = symbolConfig;
                }
            }
            
            return config;
        }
    }
}