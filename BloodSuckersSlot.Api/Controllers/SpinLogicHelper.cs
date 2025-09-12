using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared;
using System.Threading;

// STABLE RTP BALANCING IMPLEMENTATION: CONSISTENT AND PREDICTABLE PERFORMANCE
// 
// PHASE 1: Conservative RTP Recovery & Reduction
// - STRICT UPPER LIMIT: Maximum 120% of target RTP (105.6% for 88% target)
// - STRICT LOWER LIMIT: Minimum 70% of target RTP (61.6% for 88% target)
// - Conservative thresholds: 80%, 90%, 100% of target
// - Gradual recovery over multiple spins
// - Smooth transitions to prevent volatility spikes
// 
// PHASE 2: Controlled Volatility Patterns
// - Limited volatility events every 10+ spins (20% chance) - Infrequent
// - Moderate high volatility: Use reel sets with 100-120% RTP (controlled)
// - Moderate low volatility: Use reel sets with 70-90% RTP (controlled)
// - Creates natural variation without extreme swings
// 
// PHASE 3: Balanced Performance Gaps
// - Occasional dry spells every 15+ spins (15% chance) - Use 70-85% RTP
// - Occasional hot streaks every 20+ spins (10% chance) - Use 95-120% RTP
// - Creates natural variation while maintaining stability
// 
// PHASE 4: STABLE BALANCED PATTERN
// - Gradual RTP adjustments based on current performance
// - Smooth transitions between RTP ranges
// - Emergency RTP correction only after 5+ consecutive extreme spins
// - Conservative Emergency: Force 80-100% RTP after 3 spins above 110%

namespace BloodSuckersSlot.Api.Controllers
{
    public class SpinLogicHelper
    {
        private readonly Random _rng = new();
        private readonly ILogger<SpinLogicHelper> _logger;
        
        // Per-instance variables instead of static - each player gets their own instance
        private int spinCounter = 0;
        private int _freeSpinsRemaining = 0;
        private int _freeSpinsAwarded = 0; // Track free spins awarded in current session
        private int _totalFreeSpinsAwarded = 0; // Track total free spins awarded
        private int _totalBonusesTriggered = 0; // Track total bonuses triggered
        private double _totalBet = 0;
        private double _totalWin = 0;
        private int _hitCount = 0;
        // Volatility tracking
        private List<double> _recentWins = new List<double>();
        private int _maxRecentWins = 100; // Keep last 100 wins for volatility calculation
        
        // RTP BALANCING tracking variables
        private int _consecutiveLowRtpSpins = 0;
        private int _consecutiveHighRtpSpins = 0;
        private int _consecutiveAboveTargetSpins = 0; // Track spins above target

        // Constructor for Dependency Injection
        public SpinLogicHelper(ILogger<SpinLogicHelper> logger)
        {
            _logger = logger;
        }

        // Malfunction detection is now handled in SlotEvaluationService

        // FIXED: Remove hardcoded symbol configs - use GameConfig.Symbols like original SlotEngine

        public (SpinResult Result, string[][] Grid, ReelSet ChosenSet, List<WinningLine> WinningLines) SpinWithReelSets(GameConfig config, int betAmount, List<ReelSet> reelSetsFromDb, double currentRtp = 0, double currentHitRate = 0)
        {
            List<ReelSet> healthySets = new();
            bool isFreeSpin = _freeSpinsRemaining > 0;
            double currentRtpBeforeSpin = currentRtp; // Use session-based RTP instead of global
            double currentHitRateBeforeSpin = currentHitRate; // Use session-based Hit Rate instead of global
            double currentVolatility = CalculateCurrentVolatility();

            // Update max recent wins from config
            _maxRecentWins = config.MaxRecentWinsForVolatility;

            // REMOVED: Free spin RTP guard - allowing free spins to have naturally high RTP
            // This is normal behavior for free spins in slot games

            // FIXED: Handle free spin state properly
            if (isFreeSpin)
            {
                _freeSpinsRemaining--;
            }

            spinCounter++;

            var reelSets = reelSetsFromDb;
            if (isFreeSpin)
            {
                reelSets = reelSets.Where(r => r.Name != null && r.Name.StartsWith("MidRtp")).ToList();
            }

            // Calculate weights for all reel sets based on actual ExpectedRtp and EstimatedHitRate values
            foreach (var reelSet in reelSets)
            {
                // RTP weight: closer to target = higher weight
                double rtpWeight = CalculateRtpWeight(reelSet.ExpectedRtp, config.RtpTarget, currentRtpBeforeSpin);
                
                // Hit Rate weight: closer to target = higher weight  
                double hitRateWeight = CalculateHitRateWeight(reelSet.EstimatedHitRate, config.TargetHitRate, currentHitRateBeforeSpin);
                
                // Volatility weight: consider the reel set's impact on current volatility
                double volatilityWeight = CalculateVolatilityWeight(reelSet, currentVolatility, config);
                
                // Combined weight using configurable multipliers
                reelSet.RtpWeight = rtpWeight;
                reelSet.HitWeight = hitRateWeight;
                
                // Store combined weight for selection
                reelSet.CombinedWeight = (rtpWeight * config.RtpWeightMultiplier) + 
                                        (hitRateWeight * config.HitRateWeightMultiplier) + 
                                        (volatilityWeight * config.VolatilityWeightMultiplier);
            }

            // Intelligent reel set selection based on current performance
            ReelSet chosenSet = SelectOptimalReelSet(reelSets, currentRtpBeforeSpin, currentHitRateBeforeSpin, currentVolatility, config);

            if (chosenSet == null)
            {
                // Fallback: select random reel set if no optimal one found
                chosenSet = reelSets[_rng.Next(reelSets.Count)];
                Console.WriteLine($"⚠️ FALLBACK: No optimal reel set found, using random selection");
            }
            
            // 🚀 DEBUG: Log the chosen reel set details
            Console.WriteLine($"🎯 CHOSEN REEL SET: {chosenSet.Name} | Expected RTP: {chosenSet.ExpectedRtp:P2} | Estimated Hit Rate: {chosenSet.EstimatedHitRate:P2}");

            // FIXED: Add debug logging for reel set selection
            // FIXED: Add debug logging for reel set selection
            // Console.WriteLine($"🎯 REEL SET SELECTION: Current RTP: {currentRtpBeforeSpin:P2}, Target: {config.RtpTarget:P2}");
            // Console.WriteLine($"🎯 REEL SET SELECTION: Current Hit Rate: {currentHitRateBeforeSpin:P2}, Target: {config.TargetHitRate:P2}");
            // Console.WriteLine($"🎯 REEL SET SELECTION: Chosen: {chosenSet.Name} | Expected RTP: {chosenSet.ExpectedRtp:P2} | Estimated Hit Rate: {chosenSet.EstimatedHitRate:P2}");
            // Console.WriteLine($"⚖️ WEIGHT CALCULATION: RTP Weight: {chosenSet.RtpWeight:F3} × {config.RtpWeightMultiplier:F1} = {chosenSet.RtpWeight * config.RtpWeightMultiplier:F3}");
            // Console.WriteLine($"⚖️ WEIGHT CALCULATION: Hit Rate Weight: {chosenSet.HitWeight:F3} × {config.HitRateWeightMultiplier:F1} = {chosenSet.HitWeight * config.HitRateWeightMultiplier:F3}");
            // Console.WriteLine($"⚖️ WEIGHT CALCULATION: Combined Weight: {chosenSet.CombinedWeight:F3}");
            
            // 🚀 DEBUG: Log RTP selection strategy for monitoring
            Console.WriteLine($"🎯 RTP ANALYSIS: Current={currentRtpBeforeSpin:P2}, Target={config.RtpTarget:P2}, Difference={currentRtpBeforeSpin - config.RtpTarget:P2}");
            
            if (currentRtpBeforeSpin < config.RtpTarget * 0.85)
            {
                Console.WriteLine($"🚨 ULTRA LOW RTP: {currentRtpBeforeSpin:P2} < {config.RtpTarget * 0.85:P2} - FORCING HIGH RTP REEL SETS");
            }
            else if (currentRtpBeforeSpin < config.RtpTarget * 0.9)
            {
                Console.WriteLine($"⚠️ LOW RTP: {currentRtpBeforeSpin:P2} < {config.RtpTarget * 0.9:P2} - AGGRESSIVE RECOVERY");
            }
            else if (currentRtpBeforeSpin < config.RtpTarget * 0.98)
            {
                Console.WriteLine($"📈 RTP RECOVERY: {currentRtpBeforeSpin:P2} < {config.RtpTarget * 0.98:P2} - TARGETED RECOVERY");
            }
            else if (currentRtpBeforeSpin > config.RtpTarget * 1.02)
            {
                Console.WriteLine($"📉 RTP REDUCTION: {currentRtpBeforeSpin:P2} > {config.RtpTarget * 1.02:P2} - REDUCING RTP");
            }
            else
            {
                Console.WriteLine($"⚖️ RTP BALANCED: {currentRtpBeforeSpin:P2} within acceptable range");
            }

            var grid = SlotEvaluationService.SpinReels(chosenSet.Reels);
            var winningLines = new List<WinningLine>();

            // OFFICIAL BLOODSUCKERS MALFUNCTION RULE: Check for malfunctions before processing
            // TEMPORARILY DISABLED - malfunction detection is incorrectly preventing winning lines from being processed
            // if (SlotEvaluationService.DetectMalfunction(grid, config.Symbols))
            // {
            //     Console.WriteLine("MALFUNCTION: All pays and plays are voided!");
            //     return (new SpinResult
            //     {
            //         TotalWin = 0,
            //         LineWin = 0,
            //         WildWin = 0,
            //         ScatterWin = 0,
            //         BonusWin = 0,
            //         ScatterCount = 0,
            //         BonusLog = "MALFUNCTION: All pays voided",
            //         IsFreeSpin = isFreeSpin,
            //         BonusTriggered = false,
            //         FreeSpinsRemaining = _freeSpinsRemaining,
            //         FreeSpinsAwarded = _freeSpinsAwarded,
            //         TotalFreeSpinsAwarded = _totalFreeSpinsAwarded,
            //         TotalBonusesTriggered = _totalBonusesTriggered,
            //         SpinType = "MALFUNCTION"
            //     }, grid, chosenSet, new List<WinningLine>());
            // }

            // Debug: Show the grid layout
// PERFORMANCE: Console.WriteLine removed for speed
            for (int row = 0; row < 3; row++)
            {
                var rowStr = "";
                for (int col = 0; col < 5; col++)
                {
                    rowStr += $"{grid[col][row],-6} ";
                }
// PERFORMANCE: Console.WriteLine removed for speed
            }

            // Evaluate wins and collect winning lines - Using shared evaluation service
            var lineWin = SlotEvaluationService.EvaluatePaylinesWithLines(grid, config.Paylines, config.Symbols, out var lineWinningLines);
            var wildWin = SlotEvaluationService.EvaluateWildLineWinsWithLines(grid, config.Paylines, config.Symbols, out var wildWinningLines);
            var scatterWin = SlotEvaluationService.EvaluateScattersWithLines(grid, config.Symbols, isFreeSpin, out var scatterWinningLines, out var scatterCount, betAmount);

            // FIXED: Handle free spin triggering from scatter evaluation
            int freeSpinsAwarded = 0;
            if (scatterCount >= 3 && !isFreeSpin)
            {
                // Award free spins based on scatter count (same logic as original SlotEngine)
                switch (scatterCount)
                {
                    case 3:
                        freeSpinsAwarded = 10;
                        break;
                    case 4:
                        freeSpinsAwarded = 10;
                        break;
                    case 5:
                        freeSpinsAwarded = 10;
                        break;
                }
                
                if (freeSpinsAwarded > 0)
                {
                    _freeSpinsRemaining += freeSpinsAwarded;
                    _freeSpinsAwarded += freeSpinsAwarded;
                    _totalFreeSpinsAwarded += freeSpinsAwarded;
// PERFORMANCE: Console.WriteLine removed for speed
                }
            }

            // FIXED: Remove duplicate wins - if a symbol is processed by wild evaluation, remove it from line evaluation
            // We need to check not just the symbol, but also the payline to avoid removing wins from different paylines
            var wildProcessedSymbolsOnPaylines = new Dictionary<string, Dictionary<int, double>>(); // symbol -> payline -> wild win amount
            foreach (var wildLine in wildWinningLines)
            {
                if (!wildProcessedSymbolsOnPaylines.ContainsKey(wildLine.Symbol))
                    wildProcessedSymbolsOnPaylines[wildLine.Symbol] = new Dictionary<int, double>();
                wildProcessedSymbolsOnPaylines[wildLine.Symbol][wildLine.PaylineIndex] = wildLine.WinAmount;
            }
            
            // Remove line wins for symbols that were already processed by wild evaluation on the same payline
            var filteredLineWinningLines = new List<WinningLine>();
            foreach (var line in lineWinningLines)
            {
                bool shouldKeep = true;
                if (wildProcessedSymbolsOnPaylines.ContainsKey(line.Symbol))
                {
                    if (wildProcessedSymbolsOnPaylines[line.Symbol].ContainsKey(line.PaylineIndex))
                    {
                        double wildWinAmount = wildProcessedSymbolsOnPaylines[line.Symbol][line.PaylineIndex];
                        if (wildWinAmount >= line.WinAmount)
                        {
                            shouldKeep = false;
// PERFORMANCE: Console.WriteLine removed for speed
                        }
                        else
                        {
// PERFORMANCE: Console.WriteLine removed for speed
                        }
                    }
                }
                if (shouldKeep)
                {
                    filteredLineWinningLines.Add(line);
                }
            }
            lineWinningLines = filteredLineWinningLines;
            
            // Recalculate lineWin after removing duplicates
            lineWin = lineWinningLines.Sum(line => line.WinAmount);

            // Apply free spin tripling rule: Wins are tripled on free spins (except free spins or amounts won in bonus games)
            var totalWin = (lineWin + wildWin + scatterWin) * (isFreeSpin ? 3 : 1);

            // Combine all winning lines
            winningLines.AddRange(lineWinningLines);
            winningLines.AddRange(wildWinningLines);
            winningLines.AddRange(scatterWinningLines);

            // Generate SVG paths for winning lines
            foreach (var line in winningLines)
            {
                line.SvgPath = SlotEvaluationService.CreateSvgPath(line.Positions);
            }

            var bonusWin = 0.0;
            var bonusLog = "";

            if (SlotEvaluationService.CheckBonusTrigger(grid, config.Paylines, config.Symbols, scatterCount, ref bonusLog))
            {
                bonusWin = SlotEvaluationService.SimulateBonusGame(config, currentRtpBeforeSpin);
                totalWin += bonusWin;
                _totalBonusesTriggered++; // FIXED: Increment bonus counter when bonus is triggered
// PERFORMANCE: Console.WriteLine removed for speed
            }

            // FIXED: Apply win caps like original SlotEngine
            double maxMultiplier = 75.0; // Cap win to 75x of bet
            totalWin = Math.Min(totalWin, betAmount * maxMultiplier);

            // REMOVED: Early spin control - allowing natural big wins to occur
            // This allows for more realistic slot game behavior

            _totalBet += betAmount;
            _totalWin += totalWin;

            if (totalWin > 0)
                _hitCount++;

            // Update volatility tracking
            UpdateVolatilityTracking(totalWin, betAmount);
            
            // Update RTP balancing counters
            UpdateRtpBalancingCounters(currentRtpBeforeSpin, config.RtpTarget);

            // FIXED: Add debugging information like original SlotEngine
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
// PERFORMANCE: Console.WriteLine removed for speed
            if (currentVolatility > config.VolatilityThreshold)
            {
// PERFORMANCE: Console.WriteLine removed for speed
            }
            
            // PHASE 1-3: Enhanced logging for dynamic behavior
// PERFORMANCE: Console.WriteLine removed for speed
            if (Math.Abs(GetActualRtp() - config.RtpTarget) < 0.02)
            {
// PERFORMANCE: Console.WriteLine removed for speed
            }
            
            // FIXED: Add scatter and free spin debug info
            if (scatterCount >= 3)
            {
// PERFORMANCE: Console.WriteLine removed for speed
            }

            var result = new SpinResult
            {
                TotalWin = totalWin,
                LineWin = lineWin,
                WildWin = wildWin,
                ScatterWin = scatterWin,
                BonusWin = bonusWin,
                ScatterCount = scatterCount,
                BonusLog = bonusLog,
                IsFreeSpin = isFreeSpin,
                BonusTriggered = !string.IsNullOrEmpty(bonusLog),
                
                // FIXED: Add free spin and bonus tracking information
                FreeSpinsRemaining = _freeSpinsRemaining,
                FreeSpinsAwarded = _freeSpinsAwarded,
                TotalFreeSpinsAwarded = _totalFreeSpinsAwarded,
                TotalBonusesTriggered = _totalBonusesTriggered,
                SpinType = isFreeSpin ? "FREE SPIN" : "PAID SPIN"
            };

            return (result, grid, chosenSet, winningLines);
        }

        // 🏠 ULTRA-AGGRESSIVE HOUSE PROTECTION OVERRIDE: ANY RTP above target = EMERGENCY
        private ReelSet? CheckHouseProtectionOverride(List<ReelSet> reelSets, double currentRtp, GameConfig config)
        {
            // If RTP is ANY amount above target, use ONLY the lowest RTP reel sets
            if (currentRtp > config.RtpTarget) // Above 88% - ANY AMOUNT!
            {
                Console.WriteLine($"🏠 ULTRA-AGGRESSIVE HOUSE PROTECTION OVERRIDE: RTP {currentRtp:P2} > Target {config.RtpTarget:P2} - EMERGENCY LOW RTP ONLY!");
                
                // Calculate how far above target we are
                double excessRtp = currentRtp - config.RtpTarget;
                double excessPercentage = excessRtp / config.RtpTarget;
                
                if (excessPercentage > 0.15) // More than 15% above target (above 101.2%)
                {
                    // EXTREME EMERGENCY: Use ONLY the lowest RTP sets
                    var extremeEmergencySets = reelSets
                        .Where(r => r.ExpectedRtp <= config.RtpTarget * 0.4) // 35.2% or lower - EXTREME!
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(30) // Only the absolute lowest RTP sets
                        .ToList();
                    
                    if (extremeEmergencySets.Any())
                    {
                        Console.WriteLine($"🏠 EXTREME EMERGENCY OVERRIDE: Using {extremeEmergencySets.Count} extreme low RTP reel sets");
                        return ChooseWeightedByCombinedScore(extremeEmergencySets);
                    }
                }
                else if (excessPercentage > 0.05) // More than 5% above target (above 92.4%)
                {
                    // EMERGENCY: Use low RTP sets
                    var emergencySets = reelSets
                        .Where(r => r.ExpectedRtp <= config.RtpTarget * 0.5) // 44% or lower - EMERGENCY!
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(40) // Only low RTP sets
                        .ToList();
                    
                    if (emergencySets.Any())
                    {
                        Console.WriteLine($"🏠 EMERGENCY OVERRIDE: Using {emergencySets.Count} emergency low RTP reel sets");
                        return ChooseWeightedByCombinedScore(emergencySets);
                    }
                }
                else // Slightly above target (88%-92.4%)
                {
                    // FORCED LOW: Use below-target RTP sets
                    var forcedLowSets = reelSets
                        .Where(r => r.ExpectedRtp <= config.RtpTarget * 0.7) // 61.6% or lower - FORCED LOW!
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(50) // Below target RTP sets
                        .ToList();
                    
                    if (forcedLowSets.Any())
                    {
                        Console.WriteLine($"🏠 FORCED LOW OVERRIDE: Using {forcedLowSets.Count} forced low RTP reel sets");
                        return ChooseWeightedByCombinedScore(forcedLowSets);
                    }
                }
                
                // If no suitable low RTP sets found, use the absolute lowest available
                var overrideFallbackSets = reelSets
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(30)
                    .ToList();
                
                if (overrideFallbackSets.Any())
                {
                    Console.WriteLine($"🏠 FALLBACK OVERRIDE: Using {overrideFallbackSets.Count} lowest available RTP reel sets");
                    return ChooseWeightedByCombinedScore(overrideFallbackSets);
                }
            }
            
            return null; // No override needed
        }

        // 🎯 PROPER RTP BALANCING: Uses rolling averages and predictive balancing
        private ReelSet SelectOptimalReelSet(List<ReelSet> reelSets, double currentRtp, double currentHitRate, double currentVolatility, GameConfig config)
        {
            if (!reelSets.Any()) return null;
            
            // 🎯 PROPER RTP BALANCING: Use rolling average instead of cumulative
            double rollingRtp = CalculateRollingRtp();
            double rtpTrend = CalculateRtpTrend();
            
            Console.WriteLine($"🎯 PROPER BALANCING: Current RTP={currentRtp:P2}, Rolling RTP={rollingRtp:P2}, Trend={rtpTrend:F3}");
            
            // 🏠 SMART HOUSE PROTECTION: Based on rolling average and trend
            if (rollingRtp > config.RtpTarget * 1.05 || (rollingRtp > config.RtpTarget && rtpTrend > 0.01))
            {
                Console.WriteLine($"🏠 SMART HOUSE PROTECTION: Rolling RTP {rollingRtp:P2} > Target or rising trend");
                
                // Use reel sets that will bring RTP down
                var houseProtectionSets = reelSets
                    .Where(r => r.ExpectedRtp < config.RtpTarget * 0.9) // Below 79.2%
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(100)
                    .ToList();
                
                if (houseProtectionSets.Any())
                {
                    Console.WriteLine($"🏠 HOUSE PROTECTION: Using {houseProtectionSets.Count} low RTP reel sets");
                    return ChooseWeightedByCombinedScore(houseProtectionSets);
                }
            }
            
            // 🎯 SMART BALANCED SELECTION: Use predictive balancing
            Console.WriteLine($"🎯 SMART BALANCED SELECTION: Rolling RTP {rollingRtp:P2}, Trend {rtpTrend:F3}");
            
            // Use weighted selection with predictive balancing
            return ChooseWeightedByCombinedScore(reelSets);
            // use them directly instead of applying additional filtering that might override the aggressive recovery!
            
            // Check if we have a reasonable number of reel sets (indicating pre-filtering worked)
            if (reelSets.Count >= 50 && reelSets.Count <= 500)
            {
                Console.WriteLine($"🎯 USING PRE-FILTERED REEL SETS: {reelSets.Count} sets already optimized for RTP recovery");
                Console.WriteLine($"🎯 PRE-FILTERED RTP RANGE: {reelSets.Min(r => r.ExpectedRtp):P2} - {reelSets.Max(r => r.ExpectedRtp):P2}");
                
                // Use weighted selection from pre-filtered sets
                return ChooseWeightedByCombinedScore(reelSets);
            }
            
            // Only apply additional filtering if we have too many or too few reel sets
            Console.WriteLine($"⚠️ LARGE REEL SET COLLECTION: {reelSets.Count} sets - applying additional filtering");
            
            // 🚨 ULTRA-AGGRESSIVE HOUSE PROTECTION: ANY RTP above target = FORCED REDUCTION
            if (currentRtp > config.RtpTarget) // Above 88% - IMMEDIATE FORCED ACTION!
            {
                Console.WriteLine($"🚨 ULTRA-AGGRESSIVE HOUSE PROTECTION: RTP {currentRtp:P2} > Target {config.RtpTarget:P2} - FORCING LOW RTP!");
                
                // Calculate how far above target we are
                double excessRtp = currentRtp - config.RtpTarget;
                double excessPercentage = excessRtp / config.RtpTarget;
                
                if (excessPercentage > 0.1) // More than 10% above target (above 96.8%)
                {
                    // EMERGENCY REDUCTION: Use ONLY very low RTP sets
                    var emergencySets = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.3 && r.ExpectedRtp <= config.RtpTarget * 0.6) // 26.4%-52.8% - EMERGENCY LOW!
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(80)
                        .ToList();
                    
                    if (emergencySets.Any())
                    {
                        Console.WriteLine($"🚨 EMERGENCY REDUCTION: Using {emergencySets.Count} emergency low RTP reel sets");
                        return ChooseWeightedByCombinedScore(emergencySets);
                    }
                }
                else if (excessPercentage > 0.05) // More than 5% above target (above 92.4%)
                {
                    // AGGRESSIVE REDUCTION: Use low RTP sets
                    var aggressiveSets = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.4 && r.ExpectedRtp <= config.RtpTarget * 0.7) // 35.2%-61.6% - LOW!
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(100)
                        .ToList();
                    
                    if (aggressiveSets.Any())
                    {
                        Console.WriteLine($"🚨 AGGRESSIVE REDUCTION: Using {aggressiveSets.Count} low RTP reel sets");
                        return ChooseWeightedByCombinedScore(aggressiveSets);
                    }
                }
                else // Slightly above target (88%-92.4%)
                {
                    // FORCED REDUCTION: Use below-target RTP sets
                    var forcedSets = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.5 && r.ExpectedRtp <= config.RtpTarget * 0.8) // 44%-70.4% - BELOW TARGET!
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(120)
                        .ToList();
                    
                    if (forcedSets.Any())
                    {
                        Console.WriteLine($"🚨 FORCED REDUCTION: Using {forcedSets.Count} below-target RTP reel sets");
                        return ChooseWeightedByCombinedScore(forcedSets);
                    }
                }
                
                // If no suitable low RTP sets found, use the absolute lowest available
                var emergencyFallbackSets = reelSets
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(50)
                    .ToList();
                
                if (emergencyFallbackSets.Any())
                {
                    Console.WriteLine($"🚨 FALLBACK REDUCTION: Using {emergencyFallbackSets.Count} lowest available RTP reel sets");
                    return ChooseWeightedByCombinedScore(emergencyFallbackSets);
                }
            }
            
            // 🚨 FORCE REDUCTION: When RTP has been above target for too many consecutive spins
            else if (_consecutiveAboveTargetSpins >= 3) // Reduced from 5 to 3 - FASTER ACTION!
            {
                Console.WriteLine($"🚨 FORCE REDUCTION: {_consecutiveAboveTargetSpins} consecutive spins above target - FORCING LOW RTP");
                
                var forceReductionSets = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.5 && r.ExpectedRtp <= config.RtpTarget * 0.8) // 44%-70.4% - VERY LOW!
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(100)
                    .ToList();
                
                if (forceReductionSets.Any())
                {
                    Console.WriteLine($"🚨 FORCE REDUCTION: Using {forceReductionSets.Count} very low RTP reel sets");
                    return ChooseWeightedByCombinedScore(forceReductionSets);
                }
            }
            
            // 🚨 CONSERVATIVE EMERGENCY RECOVERY: When RTP is extremely low (< 30% of target)
            else if (currentRtp < config.RtpTarget * 0.3) // Below 26.4%
            {
                Console.WriteLine($"🚨 CONSERVATIVE EMERGENCY: RTP {currentRtp:P2} < {config.RtpTarget * 0.3:P2} - FORCING MODERATE HIGH RTP");
                
                var emergencySets = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 1.0 && r.ExpectedRtp <= config.RtpTarget * 1.2) // 88%-105.6% - CONTROLLED!
                    .OrderByDescending(r => r.ExpectedRtp)
                    .Take(100) // Reasonable number of sets
                    .ToList();
                
                if (emergencySets.Any())
                {
                    Console.WriteLine($"🚨 CONSERVATIVE EMERGENCY: Using {emergencySets.Count} controlled high RTP reel sets");
                    return ChooseWeightedByCombinedScore(emergencySets);
                }
            }
            
            // 🚨 MODERATE EMERGENCY RECOVERY: When RTP is critically low (< 50% of target)
            else if (currentRtp < config.RtpTarget * 0.5) // Below 44%
            {
                Console.WriteLine($"🚨 MODERATE EMERGENCY: RTP {currentRtp:P2} < {config.RtpTarget * 0.5:P2} - FORCING GOOD RTP");
                
                var emergencySets = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.9 && r.ExpectedRtp <= config.RtpTarget * 1.15) // 79.2%-101.2% - CONTROLLED!
                    .OrderByDescending(r => r.ExpectedRtp)
                    .Take(150) // More variety but still controlled
                    .ToList();
                
                if (emergencySets.Any())
                {
                    Console.WriteLine($"🚨 MODERATE EMERGENCY: Using {emergencySets.Count} controlled good RTP reel sets");
                    return ChooseWeightedByCombinedScore(emergencySets);
                }
            }
            
            // 📈 CONSERVATIVE RECOVERY: When RTP is low (< 80% of target)
            else if (currentRtp < config.RtpTarget * 0.8) // Below 70.4%
            {
                Console.WriteLine($"📈 CONSERVATIVE RECOVERY: RTP {currentRtp:P2} < {config.RtpTarget * 0.8:P2} - GRADUAL IMPROVEMENT");
                
                var recoverySets = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.85 && r.ExpectedRtp <= config.RtpTarget * 1.1) // 74.8%-96.8% - CONTROLLED!
                    .OrderByDescending(r => r.ExpectedRtp)
                    .Take(200) // More variety for gradual recovery
                    .ToList();
                
                if (recoverySets.Any())
                {
                    Console.WriteLine($"📈 CONSERVATIVE RECOVERY: Using {recoverySets.Count} controlled recovery reel sets");
                    return ChooseWeightedByCombinedScore(recoverySets);
                }
            }
            
            // 📈 GRADUAL RECOVERY: When RTP is below target but not critical
            else if (currentRtp < config.RtpTarget) // Below 88%
            {
                Console.WriteLine($"📈 GRADUAL RECOVERY: RTP {currentRtp:P2} < Target {config.RtpTarget:P2}");
                
                // Calculate recovery strength based on how far below target
                double recoveryStrength = (config.RtpTarget - currentRtp) / config.RtpTarget; // 0.0 to 1.0
                double minRtpThreshold = config.RtpTarget * (0.9 + recoveryStrength * 0.1); // 79.2% to 88%
                double maxRtpThreshold = config.RtpTarget * (1.0 + recoveryStrength * 0.1); // 88% to 96.8%
                
                var recoverySets = reelSets
                    .Where(r => r.ExpectedRtp >= minRtpThreshold && r.ExpectedRtp <= maxRtpThreshold)
                    .OrderByDescending(r => r.ExpectedRtp)
                    .Take(150) // Controlled variety
                    .ToList();
                
                if (recoverySets.Any())
                {
                    Console.WriteLine($"📈 GRADUAL RECOVERY: Using {recoverySets.Count} reel sets (range: {minRtpThreshold:P2}-{maxRtpThreshold:P2})");
                    return ChooseWeightedByCombinedScore(recoverySets);
                }
            }
            
            // 📉 AGGRESSIVE REDUCTION: When RTP is above target
            else if (currentRtp > config.RtpTarget * 1.05) // Above 92.4%
            {
                Console.WriteLine($"📉 AGGRESSIVE REDUCTION: RTP {currentRtp:P2} > {config.RtpTarget * 1.05:P2}");
                
                var reductionSets = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.6 && r.ExpectedRtp <= config.RtpTarget * 0.9) // 52.8%-79.2% - MORE AGGRESSIVE!
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(200) // More variety for better reduction
                    .ToList();
                
                if (reductionSets.Any())
                {
                    Console.WriteLine($"📉 AGGRESSIVE REDUCTION: Using {reductionSets.Count} low RTP reel sets");
                    return ChooseWeightedByCombinedScore(reductionSets);
                }
            }
            
            // 📉 MODERATE REDUCTION: When RTP is slightly above target
            else if (currentRtp > config.RtpTarget) // Above 88%
            {
                Console.WriteLine($"📉 MODERATE REDUCTION: RTP {currentRtp:P2} > Target {config.RtpTarget:P2}");
                
                var reductionSets = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.ExpectedRtp <= config.RtpTarget * 0.95) // 61.6%-83.6%
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(150)
                    .ToList();
                
                if (reductionSets.Any())
                {
                    Console.WriteLine($"📉 MODERATE REDUCTION: Using {reductionSets.Count} moderate low RTP reel sets");
                    return ChooseWeightedByCombinedScore(reductionSets);
                }
            }
            
            // 🎯 TARGET CONVERGENCE MODE: RTP is close to target, help it converge
            else
            {
                Console.WriteLine($"🎯 TARGET CONVERGENCE: RTP {currentRtp:P2} close to target {config.RtpTarget:P2}");
                
                // Calculate distance from target
                double distanceFromTarget = Math.Abs(currentRtp - config.RtpTarget) / config.RtpTarget;
                
                if (distanceFromTarget < 0.05) // Within 5% of target
                {
                    // Very close to target - use balanced selection around target
                    var convergenceSets = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.85 && r.ExpectedRtp <= config.RtpTarget * 1.05) // 74.8%-92.4%
                        .OrderBy(r => Math.Abs(r.ExpectedRtp - config.RtpTarget)) // Prefer sets closest to target
                        .Take(150)
                        .ToList();
                    
                    if (convergenceSets.Any())
                    {
                        Console.WriteLine($"🎯 CONVERGENCE: Using {convergenceSets.Count} target-focused reel sets");
                        return ChooseWeightedByCombinedScore(convergenceSets);
                    }
                }
                else
                {
                    // Slightly off target - use controlled volatility
                    double volatilityFactor = Math.Max(0.5, Math.Min(1.2, currentVolatility));
                    double rangeWidth = config.RtpTarget * 0.1 * volatilityFactor; // Smaller range
                    
                    double minPreferredRtp = Math.Max(config.RtpTarget * 0.8, config.RtpTarget - rangeWidth);
                    double maxPreferredRtp = Math.Min(config.RtpTarget * 1.05, config.RtpTarget + rangeWidth);
                    
                    var balancedSets = reelSets
                        .Where(r => r.ExpectedRtp >= minPreferredRtp && r.ExpectedRtp <= maxPreferredRtp)
                        .OrderBy(r => Math.Abs(r.ExpectedRtp - config.RtpTarget)) // Prefer sets closer to target
                        .Take(150)
                        .ToList();
                    
                    if (balancedSets.Any())
                    {
                        Console.WriteLine($"🎯 BALANCED: Using {balancedSets.Count} reel sets (range: {minPreferredRtp:P2}-{maxPreferredRtp:P2})");
                        return ChooseWeightedByCombinedScore(balancedSets);
                    }
                }
            }
            
            // 🔄 CONSERVATIVE FALLBACK: Use controlled reel sets with balanced selection
            Console.WriteLine($"🔄 CONSERVATIVE FALLBACK: Using controlled reel sets for balanced selection");
            
            // Apply strict limits even in fallback
            var conservativeFallbackSets = reelSets
                .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.ExpectedRtp <= config.RtpTarget * 1.2) // 61.6%-105.6%
                .OrderByDescending(r => r.ExpectedRtp)
                .Take(100)
                .ToList();
            
            if (conservativeFallbackSets.Any())
            {
                Console.WriteLine($"🔄 CONSERVATIVE FALLBACK: Using {conservativeFallbackSets.Count} controlled reel sets");
                return ChooseWeightedByCombinedScore(conservativeFallbackSets);
            }
            
            // Last resort: Use any available reel sets but with weighted selection
            return ChooseWeightedByCombinedScore(reelSets);
        }

        // 🎯 ROLLING RTP CALCULATION: Uses recent spins for better balancing
        private double CalculateRollingRtp()
        {
            if (_recentWins.Count < 10) return GetActualRtp(); // Fallback to cumulative if not enough data
            
            // Calculate rolling RTP from recent wins
            double recentTotalWin = _recentWins.Sum();
            double recentTotalBet = _recentWins.Count * 1.0; // Assuming 1.0 bet per spin
            
            return recentTotalBet > 0 ? recentTotalWin / recentTotalBet : GetActualRtp();
        }
        
        // 🎯 RTP TREND CALCULATION: Predicts RTP direction
        private double CalculateRtpTrend()
        {
            if (_recentWins.Count < 20) return 0.0; // Need enough data for trend
            
            // Calculate RTP trend over last 20 spins
            var recentWins = _recentWins.TakeLast(20).ToArray();
            var firstHalf = recentWins.Take(10).Average();
            var secondHalf = recentWins.Skip(10).Average();
            
            return secondHalf - firstHalf; // Positive = rising, Negative = falling
        }

        // NEW: Volatility calculation based on recent win patterns
        private double CalculateCurrentVolatility()
        {
            if (_recentWins.Count < 10) return 1.0; // Default volatility if not enough data

            var wins = _recentWins.ToArray();
            var mean = wins.Average();
            var variance = wins.Select(w => Math.Pow(w - mean, 2)).Average();
            var standardDeviation = Math.Sqrt(variance);
            
            // Normalize volatility (0 = very stable, 3+ = very volatile)
            return standardDeviation / (mean > 0 ? mean : 1.0);
        }

        // NEW: Update volatility tracking with new win
        private void UpdateVolatilityTracking(double winAmount, double betAmount)
        {
            // Normalize win amount by bet amount for consistent volatility calculation
            double normalizedWin = betAmount > 0 ? winAmount / betAmount : winAmount;
            
            _recentWins.Add(normalizedWin);
            
            // Keep only the most recent wins
            if (_recentWins.Count > _maxRecentWins)
            {
                _recentWins.RemoveAt(0);
            }
        }

        // NEW: Update RTP balancing counters
        private void UpdateRtpBalancingCounters(double currentRtp, double targetRtp)
        {
            if (currentRtp < targetRtp * 0.8) // Below 70.4%
            {
                _consecutiveLowRtpSpins++;
                _consecutiveHighRtpSpins = 0;
                _consecutiveAboveTargetSpins = 0;
            }
            else if (currentRtp > targetRtp * 1.1) // Above 96.8%
            {
                _consecutiveHighRtpSpins++;
                _consecutiveLowRtpSpins = 0;
                _consecutiveAboveTargetSpins = 0;
            }
            else if (currentRtp > targetRtp) // Above target but not extreme
            {
                _consecutiveAboveTargetSpins++;
                _consecutiveLowRtpSpins = 0;
                _consecutiveHighRtpSpins = 0;
            }
            else // Within acceptable range
            {
                _consecutiveLowRtpSpins = 0;
                _consecutiveHighRtpSpins = 0;
                _consecutiveAboveTargetSpins = 0;
            }
        }

        // 🚨 ULTRA-AGGRESSIVE HOUSE PROTECTION: RTP weight calculation for immediate house protection
        private double CalculateRtpWeight(double expectedRtp, double targetRtp, double currentRtp)
        {
            // If current RTP is above target, HEAVILY penalize ANY reel set that keeps it high
            if (currentRtp > targetRtp)
            {
                if (expectedRtp <= targetRtp) // This reel set will bring RTP down - MAXIMUM BONUS
                {
                    double reductionFactor = (currentRtp - expectedRtp) / currentRtp;
                    return 3.0 + reductionFactor; // 3.0 to 4.0 - MAXIMUM BONUS!
                }
                else if (expectedRtp < currentRtp) // This reel set reduces RTP but stays above target - HIGH BONUS
                {
                    double reductionFactor = (currentRtp - expectedRtp) / currentRtp;
                    return 2.0 + reductionFactor; // 2.0 to 3.0 - HIGH BONUS
                }
                else // This reel set keeps or increases RTP above target - MAXIMUM PENALTY
                {
                    double penaltyFactor = (expectedRtp - currentRtp) / currentRtp;
                    return Math.Max(0.001, 0.1 - penaltyFactor); // 0.001 to 0.1 - MAXIMUM PENALTY!
                }
            }
            // If current RTP is below target, normal logic applies
            else
            {
                double currentDistance = Math.Abs(currentRtp - targetRtp);
                double expectedDistance = Math.Abs(expectedRtp - targetRtp);
                
                if (expectedDistance < currentDistance)
                {
                    double improvementFactor = (currentDistance - expectedDistance) / currentDistance;
                    return 1.0 + improvementFactor; // 1.0 to 2.0
                }
                else if (expectedDistance == currentDistance)
                {
                    return 1.0;
                }
                else
                {
                    double penaltyFactor = (expectedDistance - currentDistance) / currentDistance;
                    return Math.Max(0.1, 1.0 - penaltyFactor); // 0.1 to 1.0
                }
            }
        }

        // NEW: Hit rate weight calculation based on actual values
        private double CalculateHitRateWeight(double estimatedHitRate, double targetHitRate, double currentHitRate)
        {
            // If we're below target, favor reel sets closer to target
            if (currentHitRate < targetHitRate * 0.8)
            {
                double diff = Math.Abs(estimatedHitRate - targetHitRate);
                return 1.0 / (diff + 0.01);
            }
            // If we're above target, favor reel sets closer to target
            else
            {
                double diff = Math.Abs(estimatedHitRate - targetHitRate);
                return 1.0 / (diff + 0.01);
            }
        }

        // NEW: Volatility weight calculation
        private double CalculateVolatilityWeight(ReelSet reelSet, double currentVolatility, GameConfig config)
        {
            // Estimate this reel set's volatility impact
            double estimatedVolatility = Math.Abs(reelSet.ExpectedRtp - config.RtpTarget) + Math.Abs(reelSet.EstimatedHitRate - config.TargetHitRate);
            
            // If current volatility is high, favor more stable reel sets
            if (currentVolatility > config.VolatilityThreshold)
            {
                return 1.0 / (estimatedVolatility + 0.01);
            }
            // If current volatility is low, allow some variation
            else
            {
                return 1.0 / (estimatedVolatility + 0.01);
            }
        }

        // 🚀 CRITICAL FIX: Sync SpinLogicHelper with existing session data
        public void SyncWithSessionData(double totalBet, double totalWin, int totalSpins, int winningSpins)
        {
            _totalBet = totalBet;
            _totalWin = totalWin;
            spinCounter = totalSpins;
            _hitCount = winningSpins;
            
            Console.WriteLine($"🔄 SYNC: Updated SpinLogicHelper - Bet={_totalBet:F2}, Win={_totalWin:F2}, Spins={spinCounter}, Hits={_hitCount}");
        }

        // NEW: Weighted selection using combined scores
        private ReelSet ChooseWeightedByCombinedScore(List<ReelSet> sets)
        {
            if (!sets.Any()) return null;

            // Calculate total weight
            double totalWeight = sets.Sum(s => s.CombinedWeight);
            
            if (totalWeight <= 0) return sets[_rng.Next(sets.Count)];

            // Weighted random selection
            double randomValue = _rng.NextDouble() * totalWeight;
            double currentWeight = 0;

            foreach (var set in sets)
            {
                currentWeight += set.CombinedWeight;
                if (randomValue <= currentWeight)
                {
                    return set;
                }
            }

            // Fallback
            return sets[_rng.Next(sets.Count)];
        }

        // All evaluation methods moved to SlotEvaluationService

        public double GetActualRtp() => _totalBet == 0 ? 0 : _totalWin / _totalBet;
        public double GetActualHitRate() => spinCounter == 0 ? 0 : (double)_hitCount / spinCounter;

        // Reset all instance variables to start fresh
        public void ResetAllStats()
        {
            spinCounter = 0;
            _freeSpinsRemaining = 0;
            _freeSpinsAwarded = 0;
            _totalFreeSpinsAwarded = 0;
            _totalBonusesTriggered = 0;
            _totalBet = 0;
            _totalWin = 0;
            _hitCount = 0;
            _recentWins.Clear();
            
            // Reset RTP recovery tracking variables
            _consecutiveLowRtpSpins = 0;
            _consecutiveHighRtpSpins = 0;
            _consecutiveAboveTargetSpins = 0;
            
// PERFORMANCE: Console.WriteLine removed for speed
        }
    }
}

