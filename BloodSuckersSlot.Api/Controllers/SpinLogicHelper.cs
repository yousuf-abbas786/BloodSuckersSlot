using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared;
using System.Threading;

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
            Console.WriteLine($"[VOLATILITY] Current: {currentVolatility:F4} | Recent wins: {_recentWins.Count}");
            
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

        // NEW: Intelligent reel set selection based on current performance
        private static ReelSet SelectOptimalReelSet(List<ReelSet> reelSets, double currentRtp, double currentHitRate, double currentVolatility, GameConfig config)
        {
            if (!reelSets.Any()) return null;

            // Determine what we need to improve using configurable thresholds
            bool needHigherRtp = currentRtp < config.RtpTarget * 0.9; // Below 90% of target
            bool needLowerRtp = currentRtp > config.RtpTarget * 1.1; // Above 110% of target - NEW: Actively reduce RTP
            bool needHigherHitRate = currentHitRate < config.TargetHitRate * 0.8; // Below 80% of target
            bool needLowerHitRate = currentHitRate > config.TargetHitRate * 1.1; // Above 110% of target - NEW: Actively reduce hit rate
            bool needLowerVolatility = currentVolatility > config.VolatilityThreshold; // High volatility threshold

            // Filter reel sets based on current needs
            var candidateSets = new List<ReelSet>();

            if (needHigherRtp)
            {
                // FIXED: More conservative RTP selection - choose reel sets CLOSER to target, not highest
                var rtpCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.85 && r.ExpectedRtp <= config.RtpTarget * 1.05) // 74.8% to 92.4% of target
                    .OrderBy(r => Math.Abs(r.ExpectedRtp - config.RtpTarget)) // Closest to target, not highest
                    .Take(config.MaxCandidatesPerCategory) // Use configurable limit
                    .ToList();
                
                if (rtpCandidates.Any())
                    candidateSets.AddRange(rtpCandidates);
            }
            else if (needLowerRtp)
            {
                // NEW: Actively select lower RTP reel sets when we're too high
                var lowRtpCandidates = reelSets
                    .Where(r => r.ExpectedRtp >= config.RtpTarget * 0.7 && r.ExpectedRtp <= config.RtpTarget * 0.95) // 61.6% to 83.6% of target
                    .OrderByDescending(r => r.ExpectedRtp) // Prefer higher ones in this range
                    .Take(config.MaxCandidatesPerCategory)
                    .ToList();
                
                if (lowRtpCandidates.Any())
                    candidateSets.AddRange(lowRtpCandidates);
            }

            if (needHigherHitRate)
            {
                // FIXED: More conservative hit rate selection - choose reel sets CLOSER to target, not highest
                var hitRateCandidates = reelSets
                    .Where(r => r.EstimatedHitRate >= config.TargetHitRate * 0.8 && r.EstimatedHitRate <= config.TargetHitRate * 1.1) // 36% to 49.5% of target
                    .OrderBy(r => Math.Abs(r.EstimatedHitRate - config.TargetHitRate)) // Closest to target, not highest
                    .Take(config.MaxCandidatesPerCategory) // Use configurable limit
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

            // If no specific candidates found, use all reel sets
            if (!candidateSets.Any())
            {
                candidateSets = reelSets.ToList();
            }

            // Remove duplicates and select using weighted random selection
            var uniqueCandidates = candidateSets.Distinct().ToList();
            return ChooseWeightedByCombinedScore(uniqueCandidates);
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
            Console.WriteLine("All stats reset successfully");
        }
    }
}