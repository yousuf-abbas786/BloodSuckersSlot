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
    public static class SpinLogicHelper
    {
        private static readonly Random _rng = new();
        
        // Wild paytable is now handled in SlotEvaluationService
        
        private static int spinCounter = 0;
        private static int _freeSpinsRemaining = 0;
        private static int _freeSpinsAwarded = 0; // Track free spins awarded in current session
        private static int _totalFreeSpinsAwarded = 0; // Track total free spins awarded
        private static int _totalBonusesTriggered = 0; // Track total bonuses triggered
        private static double _totalBet = 0;
        private static double _totalWin = 0;
        private static int _hitCount = 0;
        private static int _lastBonusSpin = -100;
        private static double _lastRtp = 0;
        private static bool _isSimulationMode = false;
        
        // Volatility tracking
        private static List<double> _recentWins = new List<double>();
        private static int _maxRecentWins = 100; // Keep last 100 wins for volatility calculation
        
        // PHASE 1-3: Dynamic behavior tracking variables
        private static int _rtpMomentum = 0;
        private static int _consecutiveLowRtpSpins = 0;
        private static int _spinCounter = 0;
        private static int _lastVolatilityEvent = 0;
        private static int _lastDrySpellEvent = 0;
        private static int _lastHotStreakEvent = 0;
        
        // PHASE 4: Forced wave pattern tracking variables
        private static bool _forceHighRtp = false;
        private static int _waveCounter = 0;
        
        // EMERGENCY RTP REDUCTION tracking
        private static int _consecutiveHighRtpSpins = 0;

        // Malfunction detection is now handled in SlotEvaluationService

        // FIXED: Remove hardcoded symbol configs - use GameConfig.Symbols like original SlotEngine

        public static (SpinResult Result, string[][] Grid, ReelSet ChosenSet, List<WinningLine> WinningLines) SpinWithReelSets(GameConfig config, int betAmount, List<ReelSet> reelSetsFromDb)
        {
            List<ReelSet> healthySets = new();
            bool isFreeSpin = _freeSpinsRemaining > 0;
            double currentRtpBeforeSpin = GetActualRtp();
            double currentHitRateBeforeSpin = GetActualHitRate();
            double currentVolatility = CalculateCurrentVolatility();

            // Update max recent wins from config
            _maxRecentWins = config.MaxRecentWinsForVolatility;

            // REMOVED: Free spin RTP guard - allowing free spins to have naturally high RTP
            // This is normal behavior for free spins in slot games

            // FIXED: Handle free spin state properly
            if (isFreeSpin)
            {
                _freeSpinsRemaining--;
                Console.WriteLine($"🎰 FREE SPIN EXECUTED! Remaining: {_freeSpinsRemaining}");
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
            }

            // FIXED: Add debug logging for reel set selection
            Console.WriteLine($"🎯 REEL SET SELECTION: Current RTP: {currentRtpBeforeSpin:P2}, Target: {config.RtpTarget:P2}");
            Console.WriteLine($"🎯 REEL SET SELECTION: Current Hit Rate: {currentHitRateBeforeSpin:P2}, Target: {config.TargetHitRate:P2}");
            Console.WriteLine($"🎯 REEL SET SELECTION: Chosen: {chosenSet.Name} | Expected RTP: {chosenSet.ExpectedRtp:P2} | Estimated Hit Rate: {chosenSet.EstimatedHitRate:P2}");
            Console.WriteLine($"⚖️ WEIGHT CALCULATION: RTP Weight: {chosenSet.RtpWeight:F3} × {config.RtpWeightMultiplier:F1} = {chosenSet.RtpWeight * config.RtpWeightMultiplier:F3}");
            Console.WriteLine($"⚖️ WEIGHT CALCULATION: Hit Rate Weight: {chosenSet.HitWeight:F3} × {config.HitRateWeightMultiplier:F1} = {chosenSet.HitWeight * config.HitRateWeightMultiplier:F3}");
            Console.WriteLine($"⚖️ WEIGHT CALCULATION: Combined Weight: {chosenSet.CombinedWeight:F3}");
            
            // NEW: Show RTP selection strategy
            if (currentRtpBeforeSpin < config.RtpTarget * 0.9)
            {
                Console.WriteLine($"📈 RTP RECOVERY MODE: Current RTP {currentRtpBeforeSpin:P2} < {config.RtpTarget * 0.9:P2} - Selecting HIGHER RTP reel sets");
            }
            else if (currentRtpBeforeSpin > config.RtpTarget * 1.1)
            {
                Console.WriteLine($"📉 RTP REDUCTION MODE: Current RTP {currentRtpBeforeSpin:P2} > {config.RtpTarget * 1.1:P2} - Selecting LOWER RTP reel sets");
            }
            else
            {
                Console.WriteLine($"⚖️ RTP BALANCED: Current RTP {currentRtpBeforeSpin:P2} within acceptable range");
            }

            var grid = SlotEvaluationService.SpinReels(chosenSet.Reels);
            var winningLines = new List<WinningLine>();

            // OFFICIAL BLOODSUCKERS MALFUNCTION RULE: Check for malfunctions before processing
            if (SlotEvaluationService.DetectMalfunction(grid, config.Symbols))
            {
                Console.WriteLine("MALFUNCTION: All pays and plays are voided!");
                return (new SpinResult
                {
                    TotalWin = 0,
                    LineWin = 0,
                    WildWin = 0,
                    ScatterWin = 0,
                    BonusWin = 0,
                    ScatterCount = 0,
                    BonusLog = "MALFUNCTION: All pays voided",
                    IsFreeSpin = isFreeSpin,
                    BonusTriggered = false,
                    FreeSpinsRemaining = _freeSpinsRemaining,
                    FreeSpinsAwarded = _freeSpinsAwarded,
                    TotalFreeSpinsAwarded = _totalFreeSpinsAwarded,
                    TotalBonusesTriggered = _totalBonusesTriggered,
                    SpinType = "MALFUNCTION"
                }, grid, chosenSet, new List<WinningLine>());
            }

            // Debug: Show the grid layout
            Console.WriteLine("DEBUG: Grid layout:");
            for (int row = 0; row < 3; row++)
            {
                var rowStr = "";
                for (int col = 0; col < 5; col++)
                {
                    rowStr += $"{grid[col][row],-6} ";
                }
                Console.WriteLine($"DEBUG: Row {row}: {rowStr}");
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
                    Console.WriteLine($"🎰 FREE SPINS TRIGGERED! SYM0 x{scatterCount} => +{freeSpinsAwarded} Free Spins");
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
                            Console.WriteLine($"DEBUG: Removing duplicate line win - Symbol: {line.Symbol}, PaylineIndex: {line.PaylineIndex}, LineWin: {line.WinAmount}, WildWin: {wildWinAmount}");
                        }
                        else
                        {
                            Console.WriteLine($"DEBUG: Keeping line win (higher payout) - Symbol: {line.Symbol}, PaylineIndex: {line.PaylineIndex}, LineWin: {line.WinAmount}, WildWin: {wildWinAmount}");
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
                Console.WriteLine($"🎰 BONUS TRIGGERED! Total bonuses: {_totalBonusesTriggered}");
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
            Console.WriteLine($"ReelSet: {chosenSet.Name} | Expected RTP: {chosenSet.ExpectedRtp:F4} | Estimated Hit Rate: {chosenSet.EstimatedHitRate:F4}");
            Console.WriteLine($"Line Win: {lineWin}, Wild Win: {wildWin}, Scatter Win: {scatterWin}");
            Console.WriteLine($"Total Spin Win: {totalWin} | Bet: {betAmount} | Actual RTP: {GetActualRtp():F4}");
            Console.WriteLine($"Cumulative Total Win: {_totalWin} | Total Bet: {_totalBet}");
            Console.WriteLine(isFreeSpin ? "[FREE SPIN]" : "[PAID SPIN]");
            Console.WriteLine($"Free Spins Remaining: {_freeSpinsRemaining}");
            Console.WriteLine($"Scatter Count This Spin: {scatterCount}");
            Console.WriteLine($"Total Free Spins Awarded So Far: {_totalFreeSpinsAwarded}");
            Console.WriteLine($"[HIT RATE] {_hitCount} / {spinCounter} spins ({(100.0 * _hitCount / spinCounter):F2}%)");
            Console.WriteLine($"[VOLATILITY] Current: {currentVolatility:F4} | Threshold: {config.VolatilityThreshold:F1} | Recent wins: {_recentWins.Count}");
            if (currentVolatility > config.VolatilityThreshold)
            {
                Console.WriteLine($"⚠️ HIGH VOLATILITY DETECTED: {currentVolatility:F2} > {config.VolatilityThreshold:F1} - Recovery mode activated");
            }
            
            // PHASE 1-3: Enhanced logging for dynamic behavior
            Console.WriteLine($"[DYNAMIC BEHAVIOR] RTP: {GetActualRtp():P2} | Target: {config.RtpTarget:P2} | Deviation: {Math.Abs(GetActualRtp() - config.RtpTarget):P2}");
            if (Math.Abs(GetActualRtp() - config.RtpTarget) < 0.02)
            {
                Console.WriteLine($"🎯 RTP NEAR TARGET: Allowing natural waves and volatility events");
            }
            
            // FIXED: Add scatter and free spin debug info
            if (scatterCount >= 3)
            {
                Console.WriteLine($"🎰 SCATTER TRIGGER: {scatterCount} scatters found - Free spins should trigger!");
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

        // PHASE 1 FIX: Enhanced reel set selection with momentum and recovery boost
        private static ReelSet SelectOptimalReelSet(List<ReelSet> reelSets, double currentRtp, double currentHitRate, double currentVolatility, GameConfig config)
        {
            if (!reelSets.Any()) return null;
            
            // PHASE 1: Update momentum tracking to prevent getting stuck
            if (_lastRtp > 0)
            {
                if (currentRtp > _lastRtp)
                    _rtpMomentum = Math.Min(_rtpMomentum + 1, 5);
                else if (currentRtp < _lastRtp)
                    _rtpMomentum = Math.Max(_rtpMomentum - 1, -5);
            }
            _lastRtp = currentRtp;
            
            // Track consecutive low RTP spins for recovery boost
            if (currentRtp < config.RtpTarget * 0.8)
                _consecutiveLowRtpSpins++;
            else
                _consecutiveLowRtpSpins = 0;
                
            // Track consecutive high RTP spins for emergency reduction
            if (currentRtp > config.RtpTarget * 1.2)
                _consecutiveHighRtpSpins++;
            else
                _consecutiveHighRtpSpins = 0;

            // PHASE 1: BALANCED thresholds for natural waves - NOT TOO AGGRESSIVE
            bool needHigherRtp = currentRtp < config.RtpTarget * 0.85; // Below 85% of target - More balanced
            bool needLowerRtp = currentRtp > config.RtpTarget * 1.15; // Above 115% of target - More balanced
            bool needHigherHitRate = currentHitRate < config.TargetHitRate * 0.9; // Below 90% of target
            bool needLowerHitRate = currentHitRate > config.TargetHitRate * 1.05; // Above 105% of target
            bool needLowerVolatility = currentVolatility > config.VolatilityThreshold; // High volatility threshold
            
            // PHASE 1: Recovery boost for consecutive low RTP spins - MORE BALANCED
            bool recoveryBoost = _consecutiveLowRtpSpins >= 8; // Boost after 8 consecutive low RTP spins (increased from 5)
            if (recoveryBoost)
            {
                Console.WriteLine($"🚀 ULTRA RECOVERY BOOST ACTIVATED: {_consecutiveLowRtpSpins} consecutive low RTP spins - Forcing extreme high RTP selection");
                needHigherRtp = true; // Force RTP recovery
                needLowerRtp = false; // Disable RTP reduction
            }
            
                                     // EMERGENCY RTP REDUCTION: Force dramatic drops after 5 consecutive high RTP spins - MORE BALANCED
            bool emergencyReduction = _consecutiveHighRtpSpins >= 5; // Emergency after 5 consecutive high RTP spins (increased from 3)
            if (emergencyReduction)
            {
                Console.WriteLine($"🚨 ULTRA EMERGENCY RTP REDUCTION ACTIVATED: {_consecutiveHighRtpSpins} consecutive high RTP spins - Forcing extreme low RTP selection");
                needLowerRtp = true; // Force RTP reduction
                needHigherRtp = false; // Disable RTP recovery
            }
             
                          // Filter reel sets based on current needs
             var candidateSets = new List<ReelSet>();
             
                         // ULTRA EMERGENCY: If RTP is above 120% for more than 3 spins, force immediate reduction - MORE BALANCED
            if (currentRtp > config.RtpTarget * 1.2 && _consecutiveHighRtpSpins >= 3)
            {
                Console.WriteLine($"🚨🚨 ULTRA EMERGENCY RTP REDUCTION: RTP {currentRtp:P2} > 120% for {_consecutiveHighRtpSpins} spins - FORCING EXTREME LOW RTP");
                needLowerRtp = true; // Force RTP reduction
                needHigherRtp = false; // Disable RTP recovery
                
                // Clear all candidates and force low RTP selection
                candidateSets.Clear();
                var ultraLowCandidates = reelSets
                    .Where(r => r.ExpectedRtp <= config.RtpTarget * 0.3) // Use reel sets with 26.4% or lower RTP (more extreme)
                    .OrderBy(r => r.ExpectedRtp)
                    .Take(config.MaxCandidatesPerCategory * 3)
                    .ToList();
                
                if (ultraLowCandidates.Any())
                {
                    candidateSets.AddRange(ultraLowCandidates);
                    Console.WriteLine($"🚨🚨 ULTRA EMERGENCY: Selected {ultraLowCandidates.Count} extreme low RTP candidates with RTP {ultraLowCandidates.Min(r => r.ExpectedRtp):P0} to {ultraLowCandidates.Max(r => r.ExpectedRtp):P0}");
                    
                    // Skip all other logic and return immediately
                    var uniqueCandidates = candidateSets.Distinct().ToList();
                    return ChooseWeightedByCombinedScore(uniqueCandidates);
                }
            }

            if (needHigherRtp)
            {
                // ULTRA AGGRESSIVE RTP RECOVERY: Use ANY high RTP reel set for dramatic waves
                double minRtpThreshold;
                
                if (currentRtp < config.RtpTarget * 0.8) // Very low RTP (< 70.4%)
                {
                    // BALANCED RECOVERY: Use reel sets with RTP > 60% of target for natural recovery
                    minRtpThreshold = config.RtpTarget * 0.6; // 52.8% - Allow reel sets above 52.8% RTP
                    Console.WriteLine($"🚨 BALANCED RTP RECOVERY: Current RTP {currentRtp:P2} < 70.4% - Using reel sets above 52.8% RTP");
                }
                else if (currentRtp < config.RtpTarget * 0.9) // Low RTP (< 79.2%)
                {
                    // MODERATE RECOVERY: Use reel sets with RTP > 70% of target
                    minRtpThreshold = config.RtpTarget * 0.7; // 61.6% - Allow reel sets above 61.6% RTP
                    Console.WriteLine($"📈 MODERATE RTP RECOVERY: Current RTP {currentRtp:P2} < 79.2% - Using reel sets above 61.6% RTP");
                }
                else // Moderate low RTP (79.2% - 85%)
                {
                    // GENTLE RECOVERY: Use reel sets with RTP > 80% of target
                    minRtpThreshold = config.RtpTarget * 0.8; // 70.4% - Allow reel sets above 70.4% RTP
                    Console.WriteLine($"📊 GENTLE RTP RECOVERY: Current RTP {currentRtp:P2} < 85% - Using reel sets above 70.4% RTP");
                }
                
                var rtpCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= minRtpThreshold && r.ExpectedRtp <= config.RtpTarget * 1.5) // UPPER LIMIT for balanced recovery
                    .OrderByDescending(r => r.ExpectedRtp) // Prefer HIGHER RTP for recovery
                    .Take(config.MaxCandidatesPerCategory * 2) // Double the candidates for variety
                    .ToList();
                
                if (rtpCandidates.Any())
                {
                    candidateSets.AddRange(rtpCandidates);
                    Console.WriteLine($"🎯 SELECTED {rtpCandidates.Count} HIGH RTP CANDIDATES: RTP range {rtpCandidates.Min(r => r.ExpectedRtp):P0} to {rtpCandidates.Max(r => r.ExpectedRtp):P0}");
                }
            }
            else if (needLowerRtp)
            {
                // ULTRA AGGRESSIVE RTP REDUCTION: Force dramatic drops when RTP is too high
                double maxRtpThreshold;
                
                if (currentRtp > config.RtpTarget * 1.3) // Very high RTP (> 114.4%)
                {
                    // ULTRA EXTREME REDUCTION: Use ANY reel set with RTP < 40% of target for dramatic drop
                    maxRtpThreshold = config.RtpTarget * 0.4; // 35.2% - Allow ANY reel set below 35.2% RTP
                    Console.WriteLine($"🚨 ULTRA EXTREME RTP REDUCTION: Current RTP {currentRtp:P2} > 114.4% - Using ANY reel set below 35.2% RTP for dramatic drop");
                }
                else if (currentRtp > config.RtpTarget * 1.1) // High RTP (> 96.8%)
                {
                    // EXTREME REDUCTION: Use ANY reel set with RTP < 50% of target
                    maxRtpThreshold = config.RtpTarget * 0.5; // 44% - Allow ANY reel set below 44% RTP
                    Console.WriteLine($"📉 EXTREME RTP REDUCTION: Current RTP {currentRtp:P2} > 96.8% - Using ANY reel set below 44% RTP for dramatic drop");
                }
                else // Moderate high RTP (105% - 110%)
                {
                    // AGGRESSIVE REDUCTION: Use ANY reel set with RTP < 60% of target
                    maxRtpThreshold = config.RtpTarget * 0.6; // 52.8% - Allow ANY reel set below 52.8% RTP
                    Console.WriteLine($"📊 AGGRESSIVE RTP REDUCTION: Current RTP {currentRtp:P2} > 105% - Using ANY reel set below 52.8% RTP for dramatic drop");
                }
                
                var lowRtpCandidates = reelSets
                    .Where(r => r.ExpectedRtp <= maxRtpThreshold) // NO LOWER LIMIT - Use ANY low RTP set
                    .OrderBy(r => r.ExpectedRtp) // Prefer LOWEST RTP for fastest reduction
                    .Take(config.MaxCandidatesPerCategory * 3) // Triple the candidates for maximum variety
                    .ToList();
                
                if (lowRtpCandidates.Any())
                {
                    candidateSets.AddRange(lowRtpCandidates);
                    Console.WriteLine($"🎯 SELECTED {lowRtpCandidates.Count} LOW RTP CANDIDATES: RTP range {lowRtpCandidates.Min(r => r.ExpectedRtp):P0} to {lowRtpCandidates.Max(r => r.ExpectedRtp):P0}");
                }
            }

            if (needHigherHitRate)
            {
                // FIXED: When hit rate is low, be more aggressive in selecting higher hit rate reel sets
                var hitRateCandidates = reelSets
                    .Where(r => r.EstimatedHitRate >= config.TargetHitRate * 0.7 && r.EstimatedHitRate <= config.TargetHitRate * 1.3) // 24.5% to 45.5% of target
                    .OrderByDescending(r => r.EstimatedHitRate) // Prefer HIGHER hit rate when we're below target
                    .ToList();
                
                if (hitRateCandidates.Any())
                    candidateSets.AddRange(hitRateCandidates);
            }
            else if (needLowerHitRate)
            {
                // NEW: Actively select lower hit rate reel sets when we're too high
                var lowHitRateCandidates = reelSets
                    .Where(r => r.EstimatedHitRate >= config.TargetHitRate * 0.6 && r.EstimatedHitRate <= config.TargetHitRate * 0.9) // 27% to 40.5% of target
                    .OrderByDescending(r => r.EstimatedHitRate) // Prefer higher ones in this range
                    .Take(config.MaxCandidatesPerCategory)
                    .ToList();
                
                if (lowHitRateCandidates.Any())
                    candidateSets.AddRange(lowHitRateCandidates);
            }

            if (needLowerVolatility)
            {
                // Need lower volatility - prioritize reel sets with lower expected volatility
                var lowVolatilityCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.EstimatedHitRate >= config.TargetHitRate * 0.6)
                    .OrderBy(r => Math.Abs(r.ExpectedRtp - config.RtpTarget) + Math.Abs(r.EstimatedHitRate - config.TargetHitRate))
                    .Take(config.MaxCandidatesPerCategory / 2) // Use half the limit for volatility candidates
                    .ToList();
                
                if (lowVolatilityCandidates.Any())
                    candidateSets.AddRange(lowVolatilityCandidates);
            }

            // PHASE 1: Momentum-based selection to prevent getting stuck
            if (!candidateSets.Any())
            {
                candidateSets = reelSets.ToList();
            }
            
            // PHASE 1: Add momentum-based bias to prevent RTP from getting stuck
            if (_rtpMomentum >= 3 && currentRtp > config.RtpTarget * 0.95)
            {
                // RTP is rising and near target - allow some overshoot for natural waves
                Console.WriteLine($"🌊 MOMENTUM WAVE: RTP rising (momentum: {_rtpMomentum}) - Allowing overshoot for natural waves");
                var momentumCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.9 && r.ExpectedRtp <= config.RtpTarget * 1.3)
                    .OrderByDescending(r => r.ExpectedRtp)
                    .Take(config.MaxCandidatesPerCategory)
                    .ToList();
                
                if (momentumCandidates.Any())
                    candidateSets.AddRange(momentumCandidates);
            }
            else if (_rtpMomentum <= -3 && currentRtp < config.RtpTarget * 1.05)
            {
                // RTP is falling and near target - allow some undershoot for natural waves
                Console.WriteLine($"🌊 MOMENTUM WAVE: RTP falling (momentum: {_rtpMomentum}) - Allowing undershoot for natural waves");
                var momentumCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.ExpectedRtp <= config.RtpTarget * 1.1)
                    .OrderByDescending(r => r.ExpectedRtp)
                    .Take(config.MaxCandidatesPerCategory)
                    .ToList();
                
                if (momentumCandidates.Any())
                    candidateSets.AddRange(momentumCandidates);
            }
            
            // PHASE 2: Add random volatility events for dynamic waves - LESS FREQUENT FOR BALANCE
            _spinCounter++;
            
            // Reduced random volatility events for more balanced wave patterns
            if (_spinCounter - _lastVolatilityEvent > 5 && _rng.Next(100) < 30) // 30% chance every spin after 5 (less frequent)
            {
                _lastVolatilityEvent = _spinCounter;
                bool isHighVolatility = _rng.Next(2) == 0;
                
                if (isHighVolatility)
                {
                    Console.WriteLine($"⚡ HIGH VOLATILITY EVENT: Forcing high RTP selection for waves");
                    var highVolCandidates = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 1.2 && r.ExpectedRtp <= config.RtpTarget * 3.0) // 105.6% to 264% - Wider range for more variety
                        .OrderByDescending(r => r.ExpectedRtp)
                        .Take(config.MaxCandidatesPerCategory * 2)
                        .ToList();
                    
                    if (highVolCandidates.Any())
                    {
                        candidateSets.AddRange(highVolCandidates);
                        Console.WriteLine($"🔥 HIGH VOLATILITY: Selected {highVolCandidates.Count} candidates with RTP {highVolCandidates.Min(r => r.ExpectedRtp):P0} to {highVolCandidates.Max(r => r.ExpectedRtp):P0}");
                    }
                }
                else
                {
                    Console.WriteLine($"⚡ LOW VOLATILITY EVENT: Forcing low RTP selection for waves");
                    var lowVolCandidates = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.2 && r.ExpectedRtp <= config.RtpTarget * 0.8) // 17.6% to 70.4% - Wider range for more variety
                        .OrderBy(r => r.ExpectedRtp) // Prefer lowest RTP for maximum contrast
                        .Take(config.MaxCandidatesPerCategory * 2)
                        .ToList();
                    
                    if (lowVolCandidates.Any())
                    {
                        candidateSets.AddRange(lowVolCandidates);
                        Console.WriteLine($"🌵 LOW VOLATILITY: Selected {lowVolCandidates.Count} candidates with RTP {lowVolCandidates.Min(r => r.ExpectedRtp):P0} to {lowVolCandidates.Max(r => r.ExpectedRtp):P0}");
                    }
                }
            }
            
            // Reduced dry spells and hot streaks for more balance
            if (_spinCounter - _lastDrySpellEvent > 10 && _rng.Next(100) < 25) // 25% chance every spin after 10 (less frequent)
            {
                _lastDrySpellEvent = _spinCounter;
                Console.WriteLine($"🌵 DRY SPELL: Creating intentional low-win period for gaps");
                var drySpellCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.1 && r.ExpectedRtp <= config.RtpTarget * 0.7) // 8.8% to 61.6% - Wider range for more variety
                    .OrderBy(r => r.ExpectedRtp) // Prefer lowest RTP for maximum dry spell effect
                    .Take(config.MaxCandidatesPerCategory * 2)
                    .ToList();
                
                if (drySpellCandidates.Any())
                {
                    candidateSets.AddRange(drySpellCandidates);
                    Console.WriteLine($"🌵 DRY SPELL: Selected {drySpellCandidates.Count} candidates with RTP {drySpellCandidates.Min(r => r.ExpectedRtp):P0} to {drySpellCandidates.Max(r => r.ExpectedRtp):P0}");
                }
            }
            
            if (_spinCounter - _lastHotStreakEvent > 12 && _rng.Next(100) < 20) // 20% chance every spin after 12 (less frequent)
            {
                _lastHotStreakEvent = _spinCounter;
                Console.WriteLine($"🔥 HOT STREAK: Creating intentional high-win period for excitement");
                var hotStreakCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 1.3 && r.ExpectedRtp <= config.RtpTarget * 3.5) // 114.4% to 308% - Wider range for more variety
                    .OrderByDescending(r => r.ExpectedRtp) // Prefer highest RTP for maximum hot streak effect
                    .Take(config.MaxCandidatesPerCategory * 2)
                    .ToList();
                
                if (hotStreakCandidates.Any())
                {
                    candidateSets.AddRange(hotStreakCandidates);
                    Console.WriteLine($"🔥 ULTRA HOT STREAK: Selected {hotStreakCandidates.Count} candidates with RTP {hotStreakCandidates.Min(r => r.ExpectedRtp):P0} to {hotStreakCandidates.Max(r => r.ExpectedRtp):P0}");
                }
            }

            // SPECIAL: Always include some scatter-friendly reel sets for free spin potential
            // Free spins are crucial for player engagement - ensure they can trigger
            if (_spinCounter % 3 == 0) // Every 3rd spin, include scatter-friendly options
            {
                Console.WriteLine($"🎰 SCATTER-FRIENDLY SELECTION: Including reel sets with good scatter potential for free spins");
                var scatterFriendlyCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.ExpectedRtp <= config.RtpTarget * 1.1) // Mid-range RTP for scatters
                    .OrderBy(r => Math.Abs(r.ExpectedRtp - config.RtpTarget)) // Prefer balanced RTP
                    .Take(config.MaxCandidatesPerCategory)
                    .ToList();
                
                if (scatterFriendlyCandidates.Any())
                {
                    candidateSets.AddRange(scatterFriendlyCandidates);
                    Console.WriteLine($"🎰 SCATTER-FRIENDLY: Added {scatterFriendlyCandidates.Count} candidates with RTP {scatterFriendlyCandidates.Min(r => r.ExpectedRtp):P0} to {scatterFriendlyCandidates.Max(r => r.ExpectedRtp):P0}");
                }
            }
            
            // BALANCED FORCED WAVE PATTERN: Less frequent for natural flow
            _waveCounter++;
            
            if (_waveCounter >= 4) // Every 4 spins (less frequent for balance)
            {
                _waveCounter = 0;
                _forceHighRtp = !_forceHighRtp; // Alternate between high and low
                
                // CLEAR ALL EXISTING CANDIDATES - Force wave pattern to override everything
                candidateSets.Clear();
                
                if (_forceHighRtp)
                {
                    Console.WriteLine($"🌊 FORCED WAVE: HIGH RTP PHASE - Creating wave above target");
                    var forcedHighCandidates = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 1.1 && r.ExpectedRtp <= config.RtpTarget * 2.0) // 96.8% to 176% - Wider range for more variety
                        .OrderByDescending(r => r.ExpectedRtp)
                        .Take(config.MaxCandidatesPerCategory * 3)
                        .ToList();
                    
                    if (forcedHighCandidates.Any())
                    {
                        candidateSets.AddRange(forcedHighCandidates);
                        Console.WriteLine($"🌊 FORCED HIGH WAVE: Selected {forcedHighCandidates.Count} candidates with RTP {forcedHighCandidates.Min(r => r.ExpectedRtp):P0} to {forcedHighCandidates.Max(r => r.ExpectedRtp):P0}");
                    }
                }
                else
                {
                    Console.WriteLine($"🌊 FORCED WAVE: LOW RTP PHASE - Creating wave below target");
                    var forcedLowCandidates = reelSets
                        .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.3 && r.ExpectedRtp <= config.RtpTarget * 0.9) // 26.4% to 79.2% - Wider range for more variety
                        .OrderBy(r => r.ExpectedRtp)
                        .Take(config.MaxCandidatesPerCategory * 3)
                        .ToList();
                    
                    if (forcedLowCandidates.Any())
                    {
                        candidateSets.AddRange(forcedLowCandidates);
                        Console.WriteLine($"🌊 FORCED LOW WAVE: Selected {forcedLowCandidates.Count} candidates with RTP {forcedLowCandidates.Min(r => r.ExpectedRtp):P0} to {forcedLowCandidates.Max(r => r.ExpectedRtp):P0}");
                    }
                }
                
                // Skip all other logic and return immediately
                var uniqueCandidates = candidateSets.Distinct().ToList();
                return ChooseWeightedByCombinedScore(uniqueCandidates);
            }

            // Remove duplicates and select using weighted random selection
            candidateSets = candidateSets.Distinct().ToList();
            return ChooseWeightedByCombinedScore(candidateSets);
        }

        // NEW: Volatility calculation based on recent win patterns
        private static double CalculateCurrentVolatility()
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
        private static void UpdateVolatilityTracking(double winAmount, double betAmount)
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
        private static double CalculateRtpWeight(double expectedRtp, double targetRtp, double currentRtp)
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
        private static double CalculateHitRateWeight(double estimatedHitRate, double targetHitRate, double currentHitRate)
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
        private static double CalculateVolatilityWeight(ReelSet reelSet, double currentVolatility, GameConfig config)
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

        // NEW: Weighted selection using combined scores
        private static ReelSet ChooseWeightedByCombinedScore(List<ReelSet> sets)
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

        public static double GetActualRtp() => _totalBet == 0 ? 0 : _totalWin / _totalBet;
        public static double GetActualHitRate() => spinCounter == 0 ? 0 : (double)_hitCount / spinCounter;

        // Reset all static variables to start fresh
        public static void ResetAllStats()
        {
            spinCounter = 0;
            _freeSpinsRemaining = 0;
            _freeSpinsAwarded = 0;
            _totalFreeSpinsAwarded = 0;
            _totalBonusesTriggered = 0;
            _totalBet = 0;
            _totalWin = 0;
            _hitCount = 0;
            _lastBonusSpin = -100;
            _lastRtp = 0;
            _isSimulationMode = false;
            _recentWins.Clear();
            
            // PHASE 1-3: Reset dynamic behavior tracking variables
            _rtpMomentum = 0;
            _consecutiveLowRtpSpins = 0;
            _spinCounter = 0;
            _lastVolatilityEvent = 0;
            _lastDrySpellEvent = 0;
            _lastHotStreakEvent = 0;
            
            // PHASE 4: Reset forced wave pattern tracking variables
            _forceHighRtp = false;
            _waveCounter = 0;
            
            // Reset emergency RTP reduction tracking
            _consecutiveHighRtpSpins = 0;
            
            Console.WriteLine("All stats reset successfully");
        }
    }
}