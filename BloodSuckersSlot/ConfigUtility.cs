using System;
using System.Collections.Generic;
using Shared;

namespace BloodSuckersSlot
{
    public static class ConfigUtility
    {
        public static GameConfig CreateConfiguration(string preset = "balanced")
        {
            var config = preset.ToLower() switch
            {
                "high" or "highvolatility" => GameConfig.CreateHighVolatility(),
                "low" or "lowvolatility" => GameConfig.CreateLowVolatility(),
                "balanced" or "default" => GameConfig.CreateBalanced(),
                _ => GameConfig.CreateBalanced()
            };
            
            // Initialize symbols and paylines
            InitializeSymbols(config);
            InitializePaylines(config);
            
            return config;
        }
        
        public static GameConfig CreateCustomConfiguration(
            double rtpTarget = 0.88,
            double hitRateTarget = 0.35,
            double rtpTolerance = 0.05,
            double hitRateTolerance = 0.15,
            double highRtpThreshold = 1.05,
            double criticalRtpThreshold = 1.20,
            double lowRtpThreshold = 0.75,
            int reelSetsToGenerate = 50,
            int monteCarloSpins = 1000)
        {
            var config = new GameConfig
            {
                RtpTarget = rtpTarget,
                TargetHitRate = hitRateTarget,
                RtpTolerance = rtpTolerance,
                HitRateTolerance = hitRateTolerance,
                HighRtpThreshold = highRtpThreshold,
                CriticalRtpThreshold = criticalRtpThreshold,
                LowRtpThreshold = lowRtpThreshold,
                ReelSetsToGenerate = reelSetsToGenerate,
                MonteCarloSpins = monteCarloSpins
            };
            
            // Initialize symbols and paylines
            InitializeSymbols(config);
            InitializePaylines(config);
            
            return config;
        }
        
        private static void InitializeSymbols(GameConfig config)
        {
            config.Symbols = new Dictionary<string, SymbolConfig>
            {
                ["SYM0"] = new SymbolConfig { IsScatter = true, Payouts = new Dictionary<int, double> { [3] = 5, [4] = 25, [5] = 100 } },
                ["SYM1"] = new SymbolConfig { IsWild = true, Payouts = new Dictionary<int, double> { [2] = 2, [3] = 10, [4] = 50, [5] = 200 } },
                ["SYM2"] = new SymbolConfig { IsBonus = true, Payouts = new Dictionary<int, double> { [3] = 15, [4] = 75, [5] = 300 } },
                ["SYM3"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 5, [4] = 25, [5] = 100 } },
                ["SYM4"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 4, [4] = 20, [5] = 80 } },
                ["SYM5"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 3, [4] = 15, [5] = 60 } },
                ["SYM6"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 2, [4] = 10, [5] = 40 } },
                ["SYM7"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 2, [4] = 8, [5] = 30 } },
                ["SYM8"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 1, [4] = 5, [5] = 20 } },
                ["SYM9"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 1, [4] = 3, [5] = 10 } },
                ["SYM10"] = new SymbolConfig { Payouts = new Dictionary<int, double> { [3] = 1, [4] = 2, [5] = 5 } }
            };
        }
        
        private static void InitializePaylines(GameConfig config)
        {
            config.Paylines = new List<int[]>
            {
                new int[] { 1, 1, 1, 1, 1 }, // Middle row
                new int[] { 0, 0, 0, 0, 0 }, // Top row
                new int[] { 2, 2, 2, 2, 2 }, // Bottom row
                new int[] { 0, 1, 2, 1, 0 }, // V shape
                new int[] { 2, 1, 0, 1, 2 }, // Inverted V shape
                new int[] { 0, 0, 1, 2, 2 }, // Diagonal
                new int[] { 2, 2, 1, 0, 0 }, // Diagonal
                new int[] { 1, 0, 0, 0, 1 }, // Edge to edge
                new int[] { 1, 2, 2, 2, 1 }, // Edge to edge
                new int[] { 0, 1, 1, 1, 0 }, // Top middle
                new int[] { 2, 1, 1, 1, 2 }, // Bottom middle
                new int[] { 1, 0, 1, 2, 1 }, // Cross pattern
                new int[] { 1, 2, 1, 0, 1 }, // Cross pattern
                new int[] { 0, 1, 2, 2, 2 }, // Diagonal
                new int[] { 2, 1, 0, 0, 0 }, // Diagonal
                new int[] { 0, 0, 1, 2, 1 }, // Zigzag
                new int[] { 2, 2, 1, 0, 1 }, // Zigzag
                new int[] { 1, 1, 0, 1, 1 }, // Middle gap
                new int[] { 1, 1, 2, 1, 1 }, // Middle gap
                new int[] { 0, 1, 0, 1, 0 }, // Alternating top
                new int[] { 2, 1, 2, 1, 2 }  // Alternating bottom
            };
        }
        
        public static void ShowPresetOptions()
        {
            Console.WriteLine("=== AVAILABLE PRESETS ===");
            Console.WriteLine("1. balanced (default) - Balanced volatility");
            Console.WriteLine("2. highvolatility - High volatility, lower hit rate");
            Console.WriteLine("3. lowvolatility - Low volatility, higher hit rate");
            Console.WriteLine();
            Console.WriteLine("=== CUSTOM CONFIGURATION ===");
            Console.WriteLine("You can also create custom configurations with specific parameters:");
            Console.WriteLine("- RTP Target (0.0 - 1.0)");
            Console.WriteLine("- Hit Rate Target (0.0 - 1.0)");
            Console.WriteLine("- RTP Tolerance (±0.01 - 0.20)");
            Console.WriteLine("- Hit Rate Tolerance (±0.05 - 0.30)");
            Console.WriteLine("- High RTP Threshold (1.01 - 1.50)");
            Console.WriteLine("- Critical RTP Threshold (1.10 - 1.60)");
            Console.WriteLine("- Low RTP Threshold (0.50 - 0.90)");
            Console.WriteLine("========================");
        }
        
        public static GameConfig LoadFromUserInput()
        {
            Console.WriteLine("Select configuration type:");
            Console.WriteLine("1. Use preset (balanced/highvolatility/lowvolatility)");
            Console.WriteLine("2. Create custom configuration");
            Console.Write("Enter choice (1 or 2): ");
            
            var choice = Console.ReadLine()?.Trim();
            
            if (choice == "1")
            {
                Console.Write("Enter preset name (balanced/highvolatility/lowvolatility): ");
                var preset = Console.ReadLine()?.Trim() ?? "balanced";
                return CreateConfiguration(preset);
            }
            else if (choice == "2")
            {
                return CreateCustomFromUserInput();
            }
            else
            {
                Console.WriteLine("Invalid choice, using balanced preset.");
                return CreateConfiguration("balanced");
            }
        }
        
        private static GameConfig CreateCustomFromUserInput()
        {
            Console.WriteLine("Enter custom configuration values:");
            
            Console.Write("RTP Target (0.0-1.0, default 0.88): ");
            var rtpTarget = ParseDouble(Console.ReadLine(), 0.88);
            
            Console.Write("Hit Rate Target (0.0-1.0, default 0.35): ");
            var hitRateTarget = ParseDouble(Console.ReadLine(), 0.35);
            
            Console.Write("RTP Tolerance (±0.01-0.20, default 0.05): ");
            var rtpTolerance = ParseDouble(Console.ReadLine(), 0.05);
            
            Console.Write("Hit Rate Tolerance (±0.05-0.30, default 0.15): ");
            var hitRateTolerance = ParseDouble(Console.ReadLine(), 0.15);
            
            Console.Write("High RTP Threshold (1.01-1.50, default 1.05): ");
            var highRtpThreshold = ParseDouble(Console.ReadLine(), 1.05);
            
            Console.Write("Critical RTP Threshold (1.10-1.60, default 1.20): ");
            var criticalRtpThreshold = ParseDouble(Console.ReadLine(), 1.20);
            
            Console.Write("Low RTP Threshold (0.50-0.90, default 0.75): ");
            var lowRtpThreshold = ParseDouble(Console.ReadLine(), 0.75);
            
            Console.Write("Reel Sets to Generate (10-200, default 50): ");
            var reelSetsToGenerate = ParseInt(Console.ReadLine(), 50);
            
            Console.Write("Monte Carlo Spins (100-10000, default 1000): ");
            var monteCarloSpins = ParseInt(Console.ReadLine(), 1000);
            
            return CreateCustomConfiguration(
                rtpTarget, hitRateTarget, rtpTolerance, hitRateTolerance,
                highRtpThreshold, criticalRtpThreshold, lowRtpThreshold,
                reelSetsToGenerate, monteCarloSpins);
        }
        
        private static double ParseDouble(string? input, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;
            return double.TryParse(input, out var result) ? result : defaultValue;
        }
        
        private static int ParseInt(string? input, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;
            return int.TryParse(input, out var result) ? result : defaultValue;
        }
    }
} 