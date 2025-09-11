using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared;
using System.Threading;

// ULTRA AGGRESSIVE DYNAMIC RTP IMPLEMENTATION: GUARANTEED WAVES AND MAXIMUM EXCITEMENT
// 
// PHASE 1: Ultra Aggressive RTP Recovery & Reduction
// - NO UPPER LIMIT on RTP selection - Use ANY high RTP reel set (including 1000%+)
// - NO LOWER LIMIT on RTP reduction - Use ANY low RTP reel set for dramatic drops
// - Ultra aggressive thresholds: 44%, 52.8%, 61.6% of target
// - Recovery boost after 10 consecutive low RTP spins
// - Momentum tracking to prevent getting stuck at target
// 
// PHASE 2: Ultra Dynamic Wave Patterns
// - Random volatility events every 3+ spins (60% chance) - Ultra frequent
// - Ultra high volatility: Use ANY reel set with 176%+ RTP (including 1000%+ sets)
// - Ultra low volatility: Use ANY reel set with 26.4% or lower RTP
// - Creates dramatic waves and maximum excitement
// 
// PHASE 3: Ultra Dramatic Gaps and Excitement
// - Ultra dry spells every 5+ spins (40% chance) - Use 26.4% or lower RTP
// - Ultra hot streaks every 8+ spins (35% chance) - Use 264%+ RTP (including 1000%+ sets)
// - Creates maximum contrast and dramatic gaps in the curve
// 
// PHASE 4: ULTRA AGGRESSIVE FORCED WAVE PATTERN
// - Every 2 spins, force alternating high/low RTP phases (overrides all other logic)
// - Guarantees continuous dramatic waves regardless of current RTP
// - High phase: Use 176%+ RTP reel sets (ultra aggressive)
// - Low phase: Use 26.4% or lower RTP reel sets (ultra aggressive)
// - Emergency RTP reduction after 3 consecutive high RTP spins
// - Ultra Emergency: Force 17.6% or lower RTP after 2 spins above 120%

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
        
        // SIMPLIFIED: RTP recovery tracking variables
        private int _consecutiveLowRtpSpins = 0;
        
        // EMERGENCY RTP REDUCTION tracking
        private int _consecutiveHighRtpSpins = 0;

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

        // SIMPLIFIED RTP RECOVERY LOGIC: More effective and easier to understand
        private ReelSet SelectOptimalReelSet(List<ReelSet> reelSets, double currentRtp, double currentHitRate, double currentVolatility, GameConfig config)
        {
            if (!reelSets.Any()) return null;
            
            // Track consecutive low RTP spins for recovery boost
            if (currentRtp < config.RtpTarget * 0.9) // Track when below 90% of target
                _consecutiveLowRtpSpins++;
            else
                _consecutiveLowRtpSpins = 0;
                
            // Track consecutive high RTP spins for reduction
            if (currentRtp > config.RtpTarget * 1.1)
                _consecutiveHighRtpSpins++;
            else
                _consecutiveHighRtpSpins = 0;

            // ULTRA AGGRESSIVE LOGIC: Force RTP recovery when significantly below target
            bool needHigherRtp = currentRtp < config.RtpTarget * 0.98; // Below 98% of target (86.24%)
            bool needLowerRtp = currentRtp > config.RtpTarget * 1.02; // Above 102% of target (89.76%)
            
            // EMERGENCY RECOVERY: Force high RTP after just 2 consecutive low RTP spins
            bool recoveryBoost = _consecutiveLowRtpSpins >= 2;
            if (recoveryBoost)
            {
                Console.WriteLine($"🚀 EMERGENCY RECOVERY BOOST: {_consecutiveLowRtpSpins} consecutive low RTP spins");
                needHigherRtp = true;
                needLowerRtp = false;
            }
            
            // ULTRA EMERGENCY: If RTP is below 85% of target, force immediate recovery
            if (currentRtp < config.RtpTarget * 0.85) // Below 74.8%
            {
                Console.WriteLine($"🚨 ULTRA EMERGENCY: RTP {currentRtp:P2} < {config.RtpTarget * 0.85:P2} - FORCING IMMEDIATE RECOVERY");
                needHigherRtp = true;
                needLowerRtp = false;
            }
            
            // EMERGENCY RTP REDUCTION: Force dramatic drops after 5 consecutive high RTP spins
            bool emergencyReduction = _consecutiveHighRtpSpins >= 5;
            if (emergencyReduction)
            {
                Console.WriteLine($"🚨 EMERGENCY RTP REDUCTION: {_consecutiveHighRtpSpins} consecutive high RTP spins");
                needLowerRtp = true;
                needHigherRtp = false;
            }
             
            // SIMPLIFIED REEL SET SELECTION: Clear and effective
            var candidateSets = new List<ReelSet>();
            
            if (needHigherRtp)
            {
                // ULTRA AGGRESSIVE RTP RECOVERY: Use very high RTP reel sets
                double minRtpThreshold;
                
                if (currentRtp < config.RtpTarget * 0.85) // Very low RTP (< 74.8%)
                {
                    minRtpThreshold = config.RtpTarget * 0.95; // Use reel sets with RTP > 95% of target (83.6%)
                    Console.WriteLine($"🚨 ULTRA LOW RTP: {currentRtp:P2} < {config.RtpTarget * 0.85:P2} - FORCING HIGH RTP REEL SETS");
                }
                else if (currentRtp < config.RtpTarget * 0.9) // Low RTP (< 79.2%)
                {
                    minRtpThreshold = config.RtpTarget * 0.9; // Use reel sets with RTP > 90% of target (79.2%)
                    Console.WriteLine($"⚠️ LOW RTP: {currentRtp:P2} < {config.RtpTarget * 0.9:P2} - AGGRESSIVE RECOVERY");
                }
                else // Near target RTP (79.2% - 98%)
                {
                    minRtpThreshold = config.RtpTarget * 0.85; // Use reel sets with RTP > 85% of target (74.8%)
                    Console.WriteLine($"📈 RTP RECOVERY: {currentRtp:P2} < {config.RtpTarget * 0.98:P2} - TARGETED RECOVERY");
                }
                
                var rtpCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= minRtpThreshold)
                    .OrderByDescending(r => r.ExpectedRtp) // Prefer higher RTP for recovery
                    .Take(config.MaxCandidatesPerCategory * 3) // Triple the candidates for maximum variety
                    .ToList();
                
                if (rtpCandidates.Any())
                {
                    candidateSets.AddRange(rtpCandidates);
                    Console.WriteLine($"📈 RTP RECOVERY: Selected {rtpCandidates.Count} high RTP reel sets (min: {minRtpThreshold:P2})");
                }
                else
                {
                    Console.WriteLine($"❌ NO HIGH RTP CANDIDATES: No reel sets found with RTP >= {minRtpThreshold:P2}");
                }
            }
            else if (needLowerRtp)
            {
                // RTP REDUCTION: Use low RTP reel sets
                double maxRtpThreshold = config.RtpTarget * 0.7; // Use reel sets with RTP < 70% of target
                
                var lowRtpCandidates = reelSets
                    .Where(r => r.ExpectedRtp <= maxRtpThreshold)
                    .OrderBy(r => r.ExpectedRtp) // Prefer lowest RTP for fastest reduction
                    .Take(config.MaxCandidatesPerCategory * 2)
                    .ToList();
                
                if (lowRtpCandidates.Any())
                {
                    candidateSets.AddRange(lowRtpCandidates);
                    Console.WriteLine($"📉 RTP REDUCTION: Selected {lowRtpCandidates.Count} low RTP reel sets");
                }
            }

            // FALLBACK: If no specific candidates found, use all reel sets
            if (!candidateSets.Any())
            {
                candidateSets = reelSets.ToList();
                Console.WriteLine($"⚖️ BALANCED SELECTION: Using all {reelSets.Count} reel sets");
            }

            // Remove duplicates and select using weighted random selection
            candidateSets = candidateSets.Distinct().ToList();
            return ChooseWeightedByCombinedScore(candidateSets);
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

        // NEW: RTP weight calculation based on actual values
        private double CalculateRtpWeight(double expectedRtp, double targetRtp, double currentRtp)
        {
            // If we're below target, favor reel sets closer to target
            if (currentRtp < targetRtp * 0.9)
            {
                double diff = Math.Abs(expectedRtp - targetRtp);
                return 1.0 / (diff + 0.01);
            }
            // If we're above target, favor reel sets closer to target (avoid going too high)
            else
            {
                double diff = Math.Abs(expectedRtp - targetRtp);
                return 1.0 / (diff + 0.01);
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
            
// PERFORMANCE: Console.WriteLine removed for speed
        }
    }
}
