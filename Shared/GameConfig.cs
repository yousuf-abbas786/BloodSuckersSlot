using System;
using System.Collections.Generic;

namespace Shared
{
    public class GameConfig
    {
        // Core RTP and Hit Rate Targets
        public double RtpTarget { get; set; } = 0.88; // Default target RTP (88%)
        public double TargetHitRate { get; set; } = 0.35; // Target hit rate (35%) - adjustable
        // RTP Control Parameters
        public double RtpTolerance { get; set; } = 0.05; // ±5% tolerance around target
        public double MaxRtpPerSet { get; set; } = 1.3; // Maximum RTP allowed per reel set
        public double MinRtpPerSet { get; set; } = 0.5; // Minimum RTP allowed per reel set
        // Hit Rate Control Parameters
        public double HitRateTolerance { get; set; } = 0.15; // ±15% tolerance around target
        public double MaxHitRatePerSet { get; set; } = 0.8; // Maximum hit rate allowed per reel set
        public double MinHitRatePerSet { get; set; } = 0.1; // Minimum hit rate allowed per reel set
        
        // NEW: Volatility Control Parameters
        public double VolatilityThreshold { get; set; } = 2.5; // Threshold for high volatility detection
        public double VolatilityRecoveryRate { get; set; } = 0.8; // How quickly to recover from high volatility
        public int MaxRecentWinsForVolatility { get; set; } = 100; // Number of recent wins to track for volatility
        
        // NEW: Intelligent Selection Parameters
        public double RtpWeightMultiplier { get; set; } = 0.4; // Weight for RTP in combined score
        public double HitRateWeightMultiplier { get; set; } = 0.3; // Weight for hit rate in combined score
        public double VolatilityWeightMultiplier { get; set; } = 0.3; // Weight for volatility in combined score
        public int MaxCandidatesPerCategory { get; set; } = 20; // Maximum candidates per selection category
        // RTP Recovery Settings
        public double HighRtpThreshold { get; set; } = 1.05; // RTP > 105% triggers aggressive low RTP selection
        public double CriticalRtpThreshold { get; set; } = 1.20; // RTP > 120% triggers very aggressive low RTP selection
        public double LowRtpThreshold { get; set; } = 0.75; // RTP < 75% triggers high RTP selection
        // Reel Set Generation
        public int ReelSetsToGenerate { get; set; } = 50; // Number of reel sets to generate per spin
        public int MonteCarloSpins { get; set; } = 500000; // Number of spins for RTP estimation
        // Safety and Filtering
        public bool EnableScatterGuards { get; set; } = true; // Enable scatter flood protection
        public bool EnableWildGuards { get; set; } = true; // Enable wild flood protection
        public int MaxScattersPerReelSet { get; set; } = 6; // Maximum scatters allowed per reel set
        public int MaxWildsPerReelSet { get; set; } = 7; // Maximum wilds allowed per reel set
        // Free Spins Configuration
        public int BaseBetForFreeSpins { get; set; } = 25; // Used when free spins don't cost a bet
        public int MaxFreeSpinsPerSession { get; set; } = 50;
        // Betting System Configuration
        public int BaseBetPerLevel { get; set; } = 25;
        public int DefaultLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 4;
        public decimal DefaultCoinValue { get; set; } = 0.10m;
        public decimal MinCoinValue { get; set; } = 0.01m;
        public decimal MaxCoinValue { get; set; } = 0.50m;
        // Paylines
        public List<int[]> Paylines { get; set; } = new(); // List of 5-column payline patterns
        // Symbol Configuration
        public Dictionary<string, SymbolConfig> Symbols { get; set; } = new();
        // Debug and Logging
        public bool EnableDetailedLogging { get; set; } = true; // Enable detailed console logging
        public bool EnableRtpDebugging { get; set; } = false; // Enable RTP debugging information
        // Constructor with common presets
        public GameConfig() { }
        // Preset configurations for different game types
        public static GameConfig CreateHighVolatility()
        {
            return new GameConfig
            {
                RtpTarget = 0.88,
                TargetHitRate = 0.25, // Lower hit rate for high volatility
                RtpTolerance = 0.08, // Wider tolerance
                HitRateTolerance = 0.20,
                HighRtpThreshold = 1.10,
                CriticalRtpThreshold = 1.25,
                MaxRtpPerSet = 1.5,
                MinRtpPerSet = 0.4,
                VolatilityThreshold = 3.0, // Higher threshold for high volatility
                RtpWeightMultiplier = 0.4, // RTP has more influence
                HitRateWeightMultiplier = 0.3,
                VolatilityWeightMultiplier = 0.3
            };
        }
        public static GameConfig CreateLowVolatility()
        {
            return new GameConfig
            {
                RtpTarget = 0.88,
                TargetHitRate = 0.45, // Higher hit rate for low volatility
                RtpTolerance = 0.03, // Tighter tolerance
                HitRateTolerance = 0.10,
                HighRtpThreshold = 1.03,
                CriticalRtpThreshold = 1.15,
                MaxRtpPerSet = 1.2,
                MinRtpPerSet = 0.6,
                VolatilityThreshold = 2.0, // Lower threshold for low volatility
                RtpWeightMultiplier = 0.4, // RTP has more influence
                HitRateWeightMultiplier = 0.3,
                VolatilityWeightMultiplier = 0.3
            };
        }
        public static GameConfig CreateBalanced()
        {
            var config = new GameConfig
            {
                RtpTarget = 0.88,
                TargetHitRate = 0.35, // Balanced hit rate
                RtpTolerance = 0.05,
                HitRateTolerance = 0.15,
                HighRtpThreshold = 1.05,
                CriticalRtpThreshold = 1.20,
                MaxRtpPerSet = 1.3,
                MinRtpPerSet = 0.5,
                VolatilityThreshold = 2.5, // Balanced volatility threshold
                RtpWeightMultiplier = 0.4, // RTP has more influence
                HitRateWeightMultiplier = 0.3,
                VolatilityWeightMultiplier = 0.3
            };
            
            // Initialize paylines and symbols
            InitializePaylinesAndSymbols(config);
            
            return config;
        }
        
        private static void InitializePaylinesAndSymbols(GameConfig config)
        {
            // Initialize paylines (25 paylines)
            config.Paylines = new List<int[]>
            {
                new int[] { 1, 1, 1, 1, 1 }, // Middle horizontal
                new int[] { 0, 0, 0, 0, 0 }, // Top horizontal
                new int[] { 2, 2, 2, 2, 2 }, // Bottom horizontal
                new int[] { 0, 1, 2, 1, 0 }, // V shape
                new int[] { 2, 1, 0, 1, 2 }, // Inverted V
                new int[] { 0, 0, 1, 2, 2 }, // Diagonal left to right
                new int[] { 2, 2, 1, 0, 0 }, // Diagonal right to left
                new int[] { 0, 1, 1, 1, 0 }, // U shape
                new int[] { 2, 1, 1, 1, 2 }, // Inverted U
                new int[] { 1, 0, 1, 2, 1 }, // W shape
                new int[] { 1, 2, 1, 0, 1 }, // Inverted W
                new int[] { 0, 1, 0, 1, 0 }, // Zigzag
                new int[] { 2, 1, 2, 1, 2 }, // Inverted zigzag
                new int[] { 1, 1, 0, 1, 1 }, // H shape
                new int[] { 1, 1, 2, 1, 1 }, // Inverted H
                new int[] { 0, 1, 2, 2, 2 }, // Diagonal to bottom
                new int[] { 2, 1, 0, 0, 0 }, // Diagonal to top
                new int[] { 1, 2, 2, 2, 1 }, // M shape
                new int[] { 1, 0, 0, 0, 1 }, // Inverted M
                new int[] { 0, 0, 1, 1, 2 }, // Diagonal bottom left
                new int[] { 2, 2, 1, 1, 0 }, // Diagonal top right
                new int[] { 0, 1, 2, 1, 2 }, // S shape
                new int[] { 2, 1, 0, 1, 0 }, // Inverted S
                new int[] { 1, 0, 1, 2, 0 }, // Z shape
                new int[] { 1, 2, 1, 0, 2 }  // Inverted Z
            };
            
            // Initialize symbols with payouts
            config.Symbols = new Dictionary<string, SymbolConfig>
            {
                ["SYM0"] = new SymbolConfig { IsScatter = true, Payouts = new Dictionary<int, double> { { 3, 5 }, { 4, 20 }, { 5, 100 } } },
                ["SYM1"] = new SymbolConfig { IsWild = true, Payouts = new Dictionary<int, double> { { 2, 2 }, { 3, 5 }, { 4, 20 }, { 5, 100 } } },
                ["SYM2"] = new SymbolConfig { IsBonus = true, Payouts = new Dictionary<int, double> { { 3, 10 }, { 4, 50 }, { 5, 200 } } },
                ["SYM3"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 15 }, { 4, 75 }, { 5, 300 } } },
                ["SYM4"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 12 }, { 4, 60 }, { 5, 250 } } },
                ["SYM5"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 10 }, { 4, 50 }, { 5, 200 } } },
                ["SYM6"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 8 }, { 4, 40 }, { 5, 150 } } },
                ["SYM7"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 6 }, { 4, 30 }, { 5, 100 } } },
                ["SYM8"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 4 }, { 4, 20 }, { 5, 75 } } },
                ["SYM9"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 3 }, { 4, 15 }, { 5, 50 } } },
                ["SYM10"] = new SymbolConfig { Payouts = new Dictionary<int, double> { { 3, 2 }, { 4, 10 }, { 5, 25 } } }
            };
        }
        // Method to validate configuration
        public bool Validate()
        {
            if (RtpTarget <= 0 || RtpTarget > 1.0)
            {
                Console.WriteLine("[CONFIG ERROR] RtpTarget must be between 0 and 1.0");
                return false;
            }
            if (TargetHitRate <= 0 || TargetHitRate > 1.0)
            {
                Console.WriteLine("[CONFIG ERROR] TargetHitRate must be between 0 and 1.0");
                return false;
            }
            if (MaxRtpPerSet <= MinRtpPerSet)
            {
                Console.WriteLine("[CONFIG ERROR] MaxRtpPerSet must be greater than MinRtpPerSet");
                return false;
            }
            if (MaxHitRatePerSet <= MinHitRatePerSet)
            {
                Console.WriteLine("[CONFIG ERROR] MaxHitRatePerSet must be greater than MinHitRatePerSet");
                return false;
            }
            return true;
        }
        // Method to print current configuration
        public void PrintConfiguration()
        {
            Console.WriteLine("=== GAME CONFIGURATION ===");
            Console.WriteLine($"RTP Target: {RtpTarget:P1}");
            Console.WriteLine($"Hit Rate Target: {TargetHitRate:P1}");
            Console.WriteLine($"RTP Tolerance: ±{RtpTolerance:P1}");
            Console.WriteLine($"Hit Rate Tolerance: ±{HitRateTolerance:P1}");
            Console.WriteLine($"High RTP Threshold: {HighRtpThreshold:P1}");
            Console.WriteLine($"Critical RTP Threshold: {CriticalRtpThreshold:P1}");
            Console.WriteLine($"Low RTP Threshold: {LowRtpThreshold:P1}");
            Console.WriteLine($"RTP Range: {MinRtpPerSet:P1} - {MaxRtpPerSet:P1}");
            Console.WriteLine($"Hit Rate Range: {MinHitRatePerSet:P1} - {MaxHitRatePerSet:P1}");
            Console.WriteLine($"Reel Sets Generated: {ReelSetsToGenerate}");
            Console.WriteLine($"Monte Carlo Spins: {MonteCarloSpins}");
            Console.WriteLine("========================");
        }
    }
} 