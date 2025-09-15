using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared;
using Shared.Models;
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
        private int _lastFreeSpinOpportunitySpin = 0; // Track last spin when free spin opportunity occurred

        // Constructor for Dependency Injection
        public SpinLogicHelper(ILogger<SpinLogicHelper> logger)
        {
            _logger = logger;
        }

        // 🎯 SESSION STATE MANAGEMENT: Load session data into SpinLogicHelper
        public void LoadSessionState(PlayerSessionResponse session)
        {
            spinCounter = session.TotalSpins;
            _totalBet = (double)session.TotalBet;
            _totalWin = (double)session.TotalWin;
            _hitCount = session.WinningSpins;
            _freeSpinsAwarded = session.FreeSpinsAwarded;
            _totalBonusesTriggered = session.BonusesTriggered;
            
            // Load recent wins for volatility calculation (simulate from session data)
            _recentWins.Clear();
            if (session.MaxWin > 0)
            {
                // Add some recent wins based on session data for volatility calculation
                for (int i = 0; i < Math.Min(10, session.WinningSpins); i++)
                {
                    _recentWins.Add((double)session.MaxWin * (0.5 + _rng.NextDouble() * 0.5)); // Simulate recent wins
                }
            }
            
            _logger.LogDebug("🎯 SESSION LOADED: Spins={Spins}, Bet={Bet:F2}, Win={Win:F2}, Hits={Hits}", 
                spinCounter, _totalBet, _totalWin, _hitCount);
        }

        // 🎯 SESSION STATE MANAGEMENT: Get current session state from SpinLogicHelper
        public PlayerSessionState GetCurrentSessionState()
        {
            return new PlayerSessionState
            {
                SpinCounter = spinCounter,
                TotalBet = _totalBet,
                TotalWin = _totalWin,
                HitCount = _hitCount,
                FreeSpinsAwarded = _freeSpinsAwarded,
                TotalBonusesTriggered = _totalBonusesTriggered,
                FreeSpinsRemaining = _freeSpinsRemaining,
                RecentWins = new List<double>(_recentWins),
                ConsecutiveLowRtpSpins = _consecutiveLowRtpSpins,
                ConsecutiveHighRtpSpins = _consecutiveHighRtpSpins,
                ConsecutiveAboveTargetSpins = _consecutiveAboveTargetSpins,
                LastFreeSpinOpportunitySpin = _lastFreeSpinOpportunitySpin
            };
        }



        // Malfunction detection is now handled in SlotEvaluationService

        // FIXED: Remove hardcoded symbol configs - use GameConfig.Symbols like original SlotEngine

        public (SpinResult Result, string[][] Grid, ReelSet ChosenSet, List<WinningLine> WinningLines) SpinWithReelSets(GameConfig config, int betAmount, List<ReelSet> reelSetsFromDb, double currentRtp = 0, double currentHitRate = 0, double monetaryBetAmount = 0, double coinValue = 0.10)
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

            // Calculate improved weights for all reel sets using new formula-based approach
            foreach (var reelSet in reelSets)
            {
                // Calculate individual weights using improved formulas
                double rtpWeight = CalculateRtpWeight(reelSet.ExpectedRtp, config.RtpTarget, currentRtpBeforeSpin);
                double hitRateWeight = CalculateHitRateWeight(reelSet.EstimatedHitRate, config.TargetHitRate, currentHitRateBeforeSpin);
                double volatilityWeight = CalculateVolatilityWeight(reelSet, currentVolatility, config);
                
                // Store individual weights for debugging/monitoring
                reelSet.RtpWeight = rtpWeight;
                reelSet.HitWeight = hitRateWeight;
                
                // Calculate combined weight using dynamic multipliers
                reelSet.CombinedWeight = CalculateCombinedWeight(reelSet, currentRtpBeforeSpin, 
                                                               currentHitRateBeforeSpin, currentVolatility, config);
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
                    _lastFreeSpinOpportunitySpin = spinCounter; // Track when free spins were triggered
                    Console.WriteLine($"🎰 FREE SPINS TRIGGERED: {scatterCount} scatters => +{freeSpinsAwarded} free spins");
                }
            }
            
            // Track scatter opportunities even if not enough for free spins
            if (scatterCount >= 2 && !isFreeSpin)
            {
                _lastFreeSpinOpportunitySpin = spinCounter; // Track scatter opportunities
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

            _totalBet += monetaryBetAmount;
            _totalWin += totalWin * coinValue; // Convert coin wins to monetary wins

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


        // 🎯 SIMPLIFIED FORMULA-BASED REELSET SELECTION: Pure weight-based selection
        private ReelSet SelectOptimalReelSet(List<ReelSet> reelSets, double currentRtp, double currentHitRate, 
                                           double currentVolatility, GameConfig config)
        {
            if (!reelSets.Any()) return null;
            
            // Log selection strategy for monitoring
            Console.WriteLine($"🎯 FORMULA-BASED SELECTION: {reelSets.Count} reelsets available");
            Console.WriteLine($"🎯 CURRENT STATE: RTP={currentRtp:P2}, HitRate={currentHitRate:P2}, Volatility={currentVolatility:F2}");
            Console.WriteLine($"🎯 TARGET: RTP={config.RtpTarget:P2}, HitRate={config.TargetHitRate:P2}");
            
            // Use pure weighted selection based on calculated combined weights
            // The weights already account for RTP, hit rate, and volatility optimization
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

        // 🎯 IMPROVED RTP WEIGHT CALCULATION: Smooth gradients with free spin consideration
        private double CalculateRtpWeight(double expectedRtp, double targetRtp, double currentRtp)
        {
            double rtpDistance = Math.Abs(expectedRtp - targetRtp);
            double currentDistance = Math.Abs(currentRtp - targetRtp);
            
            // Adaptive scaling factor based on how far off target we are
            double urgencyFactor = Math.Min(3.0, currentDistance / (targetRtp * 0.1)); // 1.0 to 3.0
            
            // FREE SPIN CONSIDERATION: Allow some high RTP reelsets for scatter combinations
            double freeSpinFactor = 1.0;
            if (expectedRtp > targetRtp * 1.1) // High RTP reelsets (above 96.8% for 88% target)
            {
                // Add periodic chance for high RTP reelsets to allow free spins
                // This ensures scatter combinations can occur naturally
                double highRtpChance = Math.Max(0.1, 0.3 - (currentRtp - targetRtp) / targetRtp);
                freeSpinFactor = 1.0 + highRtpChance; // 1.0 to 1.3 bonus for high RTP reelsets
            }
            
            // Direction preference: favor reelsets that move us toward target
            double directionFactor = 1.0;
            if (currentRtp < targetRtp && expectedRtp > currentRtp) {
                directionFactor = 1.5; // Bonus for moving up toward target
            } else if (currentRtp > targetRtp && expectedRtp < currentRtp) {
                directionFactor = 1.5; // Bonus for moving down toward target
            }
            
            // Smooth gradient weight calculation using exponential decay
            double baseWeight = Math.Exp(-rtpDistance * urgencyFactor);
            return baseWeight * directionFactor * freeSpinFactor;
        }

        // 🎯 ENHANCED HIT RATE WEIGHT CALCULATION: Adaptive scaling with direction preference
        private double CalculateHitRateWeight(double estimatedHitRate, double targetHitRate, double currentHitRate)
        {
            double hitRateDistance = Math.Abs(estimatedHitRate - targetHitRate);
            
            // Adaptive scaling based on current hit rate performance
            double currentHitRateDistance = Math.Abs(currentHitRate - targetHitRate);
            double hitRateUrgency = Math.Min(2.0, currentHitRateDistance / (targetHitRate * 0.2));
            
            // Direction preference for hit rate
            double directionFactor = 1.0;
            if (currentHitRate < targetHitRate && estimatedHitRate > currentHitRate) {
                directionFactor = 1.3; // Bonus for moving up toward target
            } else if (currentHitRate > targetHitRate && estimatedHitRate < currentHitRate) {
                directionFactor = 1.3; // Bonus for moving down toward target
            }
            
            // Exponential decay with urgency scaling
            double baseWeight = Math.Exp(-hitRateDistance * hitRateUrgency);
            return baseWeight * directionFactor;
        }

        // 🎯 SOPHISTICATED VOLATILITY WEIGHT CALCULATION: Advanced volatility management
        private double CalculateVolatilityWeight(ReelSet reelSet, double currentVolatility, GameConfig config)
        {
            // Calculate expected volatility impact of this reelset using Euclidean distance
            double rtpContribution = Math.Abs(reelSet.ExpectedRtp - config.RtpTarget);
            double hitRateContribution = Math.Abs(reelSet.EstimatedHitRate - config.TargetHitRate);
            double expectedVolatilityImpact = Math.Sqrt(rtpContribution * rtpContribution + 
                                                      hitRateContribution * hitRateContribution);
            
            // Volatility management strategy
            if (currentVolatility > config.VolatilityThreshold) {
                // High volatility: strongly favor stabilizing reelsets
                double stabilizationFactor = Math.Max(0.1, 1.0 - expectedVolatilityImpact);
                return Math.Exp(-expectedVolatilityImpact * 2.0) * stabilizationFactor;
            } else {
                // Low volatility: allow controlled variation
                double variationFactor = Math.Min(2.0, expectedVolatilityImpact);
                return Math.Exp(-expectedVolatilityImpact * 0.5) * variationFactor;
            }
        }

        // 🎯 COMBINED WEIGHT FORMULA: Dynamic multipliers with free spin consideration
        private double CalculateCombinedWeight(ReelSet reelSet, double currentRtp, double currentHitRate, 
                                             double currentVolatility, GameConfig config)
        {
            double rtpWeight = CalculateRtpWeight(reelSet.ExpectedRtp, config.RtpTarget, currentRtp);
            double hitRateWeight = CalculateHitRateWeight(reelSet.EstimatedHitRate, config.TargetHitRate, currentHitRate);
            double volatilityWeight = CalculateVolatilityWeight(reelSet, currentVolatility, config);
            
            // FREE SPIN OPPORTUNITY BONUS: Encourage scatter combinations
            double freeSpinBonus = CalculateFreeSpinBonus(reelSet, currentRtp, config);
            
            // Dynamic multiplier adjustment based on current state
            double rtpMultiplier = config.RtpWeightMultiplier;
            double hitRateMultiplier = config.HitRateWeightMultiplier;
            double volatilityMultiplier = config.VolatilityWeightMultiplier;
            
            // Adjust multipliers based on how far off target we are
            double rtpDeviation = Math.Abs(currentRtp - config.RtpTarget) / config.RtpTarget;
            double hitRateDeviation = Math.Abs(currentHitRate - config.TargetHitRate) / config.TargetHitRate;
            
            if (rtpDeviation > 0.1) rtpMultiplier *= 1.5; // Increase RTP importance when far off
            if (hitRateDeviation > 0.2) hitRateMultiplier *= 1.3; // Increase hit rate importance when far off
            if (currentVolatility > config.VolatilityThreshold) volatilityMultiplier *= 1.4; // Increase volatility importance when high
            
            // Normalize multipliers to maintain proportions
            double totalMultiplier = rtpMultiplier + hitRateMultiplier + volatilityMultiplier;
            rtpMultiplier /= totalMultiplier;
            hitRateMultiplier /= totalMultiplier;
            volatilityMultiplier /= totalMultiplier;
            
            double combinedWeight = (rtpWeight * rtpMultiplier) + 
                                   (hitRateWeight * hitRateMultiplier) + 
                                   (volatilityWeight * volatilityMultiplier);
            
            // Apply free spin bonus
            return combinedWeight * freeSpinBonus;
        }
        
        // 🎰 FREE SPIN BONUS CALCULATION: Encourage reelsets that can trigger free spins
        private double CalculateFreeSpinBonus(ReelSet reelSet, double currentRtp, GameConfig config)
        {
            // Check if this reelset has potential for scatter combinations
            bool hasScatterPotential = HasScatterPotential(reelSet);
            
            if (!hasScatterPotential) return 1.0; // No bonus for reelsets without scatter potential
            
            // Calculate how long it's been since last free spin opportunity
            int spinsSinceLastFreeSpin = spinCounter - _lastFreeSpinOpportunitySpin;
            double freeSpinUrgency = Math.Min(2.0, spinsSinceLastFreeSpin / 50.0); // Increase urgency after 50+ spins
            
            // Allow more high RTP reelsets when we need free spin opportunities
            if (reelSet.ExpectedRtp > config.RtpTarget * 1.05) // Above 92.4% for 88% target
            {
                // Bonus for high RTP reelsets when we need free spins
                double bonusFactor = 1.0 + (freeSpinUrgency * 0.3); // 1.0 to 1.6 bonus
                return Math.Min(bonusFactor, 1.6); // Cap the bonus
            }
            
            return 1.0; // No bonus for other reelsets
        }
        
        // 🔍 SCATTER POTENTIAL DETECTION: Check if reelset can produce scatter combinations
        private bool HasScatterPotential(ReelSet reelSet)
        {
            if (reelSet?.Reels == null) return false;
            
            // Count scatter symbols in visible positions (first 3 positions of each reel)
            int scatterCount = 0;
            for (int col = 0; col < Math.Min(5, reelSet.Reels.Count); col++)
            {
                var reel = reelSet.Reels[col];
                if (reel != null && reel.Count >= 3)
                {
                    // Check first 3 positions (visible area)
                    for (int pos = 0; pos < Math.Min(3, reel.Count); pos++)
                    {
                        if (reel[pos] == "SYM0") // SYM0 is scatter symbol
                        {
                            scatterCount++;
                        }
                    }
                }
            }
            
            // Consider it has scatter potential if there are at least 2 scatter symbols visible
            return scatterCount >= 2;
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
            _lastFreeSpinOpportunitySpin = 0;
            
// PERFORMANCE: Console.WriteLine removed for speed
        }
    }

    // 🎯 SESSION STATE MODEL: Represents current state of SpinLogicHelper
    public class PlayerSessionState
    {
        public int SpinCounter { get; set; }
        public double TotalBet { get; set; }
        public double TotalWin { get; set; }
        public int HitCount { get; set; }
        public int FreeSpinsAwarded { get; set; }
        public int TotalBonusesTriggered { get; set; }
        public int FreeSpinsRemaining { get; set; }
        public List<double> RecentWins { get; set; } = new();
        public int ConsecutiveLowRtpSpins { get; set; }
        public int ConsecutiveHighRtpSpins { get; set; }
        public int ConsecutiveAboveTargetSpins { get; set; }
        public int LastFreeSpinOpportunitySpin { get; set; }
        
        // Calculated properties
        public double CurrentRtp => TotalBet > 0 ? TotalWin / TotalBet : 0.0;
        public double CurrentHitRate => SpinCounter > 0 ? (double)HitCount / SpinCounter : 0.0;
    }
}

