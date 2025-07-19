using System;

namespace BloodSuckersSlot
{
    public static class ConfigurationExamples
    {
        public static void ShowExamples()
        {
            Console.WriteLine("=== CONFIGURATION EXAMPLES ===");
            
            // Example 1: High Volatility Configuration
            var highVolConfig = ConfigUtility.CreateConfiguration("highvolatility");
            Console.WriteLine("\n1. HIGH VOLATILITY CONFIGURATION:");
            highVolConfig.PrintConfiguration();
            
            // Example 2: Low Volatility Configuration
            var lowVolConfig = ConfigUtility.CreateConfiguration("lowvolatility");
            Console.WriteLine("\n2. LOW VOLATILITY CONFIGURATION:");
            lowVolConfig.PrintConfiguration();
            
            // Example 3: Custom Configuration
            var customConfig = ConfigUtility.CreateCustomConfiguration(
                rtpTarget: 0.90,           // 90% RTP target
                hitRateTarget: 0.40,       // 40% hit rate target
                rtpTolerance: 0.03,        // ±3% RTP tolerance
                hitRateTolerance: 0.10,    // ±10% hit rate tolerance
                highRtpThreshold: 1.03,    // Trigger low RTP selection at 103%
                criticalRtpThreshold: 1.15, // Very aggressive low RTP at 115%
                lowRtpThreshold: 0.80,     // Trigger high RTP selection at 80%
                reelSetsToGenerate: 75,    // Generate 75 reel sets per spin
                monteCarloSpins: 2000      // Use 2000 spins for RTP estimation
            );
            Console.WriteLine("\n3. CUSTOM CONFIGURATION (90% RTP, 40% Hit Rate):");
            customConfig.PrintConfiguration();
            
            // Example 4: Ultra Conservative Configuration
            var conservativeConfig = ConfigUtility.CreateCustomConfiguration(
                rtpTarget: 0.85,           // 85% RTP target
                hitRateTarget: 0.50,       // 50% hit rate target
                rtpTolerance: 0.02,        // Very tight ±2% RTP tolerance
                hitRateTolerance: 0.05,    // Very tight ±5% hit rate tolerance
                highRtpThreshold: 1.02,    // Very sensitive - trigger at 102%
                criticalRtpThreshold: 1.10, // Very aggressive at 110%
                lowRtpThreshold: 0.85,     // Trigger high RTP at 85%
                reelSetsToGenerate: 100,   // Generate many reel sets
                monteCarloSpins: 5000      // Very accurate RTP estimation
            );
            Console.WriteLine("\n4. ULTRA CONSERVATIVE CONFIGURATION:");
            conservativeConfig.PrintConfiguration();
            
            Console.WriteLine("\n=== HOW TO USE ===");
            Console.WriteLine("1. In Program.cs, change the preset:");
            Console.WriteLine("   var config = ConfigUtility.CreateConfiguration(\"highvolatility\");");
            Console.WriteLine();
            Console.WriteLine("2. Or create a custom configuration:");
            Console.WriteLine("   var config = ConfigUtility.CreateCustomConfiguration(rtpTarget: 0.90, hitRateTarget: 0.40);");
            Console.WriteLine();
            Console.WriteLine("3. Or use interactive configuration:");
            Console.WriteLine("   var config = ConfigUtility.LoadFromUserInput();");
        }
        
        public static void ShowConfigurationComparison()
        {
            Console.WriteLine("=== CONFIGURATION COMPARISON ===");
            
            var balanced = ConfigUtility.CreateConfiguration("balanced");
            var highVol = ConfigUtility.CreateConfiguration("highvolatility");
            var lowVol = ConfigUtility.CreateConfiguration("lowvolatility");
            
            Console.WriteLine("| Setting | Balanced | High Vol | Low Vol |");
            Console.WriteLine("|---------|----------|----------|---------|");
            Console.WriteLine($"| RTP Target | {balanced.RtpTarget:P1} | {highVol.RtpTarget:P1} | {lowVol.RtpTarget:P1} |");
            Console.WriteLine($"| Hit Rate | {balanced.TargetHitRate:P1} | {highVol.TargetHitRate:P1} | {lowVol.TargetHitRate:P1} |");
            Console.WriteLine($"| RTP Tolerance | ±{balanced.RtpTolerance:P1} | ±{highVol.RtpTolerance:P1} | ±{lowVol.RtpTolerance:P1} |");
            Console.WriteLine($"| Hit Rate Tolerance | ±{balanced.HitRateTolerance:P1} | ±{highVol.HitRateTolerance:P1} | ±{lowVol.HitRateTolerance:P1} |");
            Console.WriteLine($"| High RTP Threshold | {balanced.HighRtpThreshold:P1} | {highVol.HighRtpThreshold:P1} | {lowVol.HighRtpThreshold:P1} |");
            Console.WriteLine($"| Critical RTP Threshold | {balanced.CriticalRtpThreshold:P1} | {highVol.CriticalRtpThreshold:P1} | {lowVol.CriticalRtpThreshold:P1} |");
            Console.WriteLine($"| Low RTP Threshold | {balanced.LowRtpThreshold:P1} | {highVol.LowRtpThreshold:P1} | {lowVol.LowRtpThreshold:P1} |");
            Console.WriteLine($"| RTP Range | {balanced.MinRtpPerSet:P1}-{balanced.MaxRtpPerSet:P1} | {highVol.MinRtpPerSet:P1}-{highVol.MaxRtpPerSet:P1} | {lowVol.MinRtpPerSet:P1}-{lowVol.MaxRtpPerSet:P1} |");
        }
    }
} 