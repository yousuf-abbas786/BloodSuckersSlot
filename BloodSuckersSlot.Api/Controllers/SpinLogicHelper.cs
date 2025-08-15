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
        private static int _spinsAboveTarget = 0;
        private static int _spinsBelowTarget = 0;
        private static int _freeSpinsRemaining = 0;
        private static int _freeSpinsAwarded = 0; // Track free spins awarded in current session
        private static int _totalFreeSpinsAwarded = 0; // Track total free spins awarded
        private static int _totalBonusesTriggered = 0; // Track total bonuses triggered
        private static double _totalBet = 0;
        private static double _totalWin = 0;
        private static int _hitCount = 0;
        private static int _lastBonusSpin = -100;
        private static int _rtpMomentum = 0;
        private static double _lastRtp = 0;
        private static bool _isSimulationMode = false;

        // Malfunction detection is now handled in SlotEvaluationService

        // FIXED: Remove hardcoded symbol configs - use GameConfig.Symbols like original SlotEngine

        public static (SpinResult Result, string[][] Grid, ReelSet ChosenSet, List<WinningLine> WinningLines) SpinWithReelSets(GameConfig config, int betAmount, List<ReelSet> reelSetsFromDb)
        {
            List<ReelSet> healthySets = new();
            bool isFreeSpin = _freeSpinsRemaining > 0;
            double currentRtpBeforeSpin = GetActualRtp();

            // Correction logic
            if (currentRtpBeforeSpin > config.RtpTarget)
            {
                _spinsAboveTarget++;
                _spinsBelowTarget = 0;
            }
            else if (currentRtpBeforeSpin < config.RtpTarget)
            {
                _spinsBelowTarget++;
                _spinsAboveTarget = 0;
            }
            else
            {
                _spinsAboveTarget = 0;
                _spinsBelowTarget = 0;
            }

            // REMOVED: Free spin RTP guard - allowing free spins to have naturally high RTP
            // This is normal behavior for free spins in slot games

            if (isFreeSpin)
                _freeSpinsRemaining--;

            spinCounter++;

            var reelSets = reelSetsFromDb;
            if (isFreeSpin)
            {
                reelSets = reelSets.Where(r => r.Name != null && r.Name.StartsWith("MidRtp")).ToList();
            }

            // Assume DB sets already have ExpectedRtp/EstimatedHitRate, but recalc weights
            foreach (var reelSet in reelSets)
            {
                reelSet.RtpWeight = CalculateWeight(reelSet.ExpectedRtp, config.RtpTarget);
                reelSet.HitWeight = CalculateWeight(reelSet.EstimatedHitRate, config.TargetHitRate);
            }

            ReelSet chosenSet = null;
            if (_spinsAboveTarget > 250)
            {
                var lowRtpSets = reelSets.Where(r => r.Name != null && r.Name.StartsWith("LowRtp")).ToList();
                if (lowRtpSets.Count > 0)
                {
                    chosenSet = ChooseWeighted(config, lowRtpSets);
                    healthySets = new();
                }
            }
            else if (_spinsBelowTarget > 150)
            {
                var highRtpSets = reelSets.Where(r => r.Name != null && r.Name.StartsWith("HighRtp")).ToList();
                if (highRtpSets.Count > 0)
                {
                    chosenSet = ChooseWeighted(config, highRtpSets);
                    healthySets = new();
                }
            }

            if (chosenSet == null)
            {
                healthySets = reelSets.Where(r => r.ExpectedRtp >= config.RtpTarget * 0.9 && r.ExpectedRtp <= config.RtpTarget * 1.1).ToList();
                if (healthySets.Count == 0)
                {
                    healthySets = reelSets;
                }
                chosenSet = ChooseWeighted(config, healthySets);
            }

            if (chosenSet == null)
            {
                chosenSet = reelSets[_rng.Next(reelSets.Count)];
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

            // FIXED: Add debugging information like original SlotEngine
            Console.WriteLine($"ReelSet: {chosenSet.Name} | Expected RTP: {chosenSet.ExpectedRtp:F4}");
            Console.WriteLine($"Line Win: {lineWin}, Wild Win: {wildWin}, Scatter Win: {scatterWin}");
            Console.WriteLine($"Total Spin Win: {totalWin} | Bet: {betAmount} | Actual RTP: {GetActualRtp():F4}");
            Console.WriteLine($"Cumulative Total Win: {_totalWin} | Total Bet: {_totalBet}");
            Console.WriteLine(isFreeSpin ? "[FREE SPIN]" : "[PAID SPIN]");
            Console.WriteLine($"Free Spins Remaining: {_freeSpinsRemaining}");
            Console.WriteLine($"Scatter Count This Spin: {scatterCount}");
            Console.WriteLine($"Total Free Spins Awarded So Far: {_totalFreeSpinsAwarded}");
            Console.WriteLine($"[HIT RATE] {_hitCount} / {spinCounter} spins ({(100.0 * _hitCount / spinCounter):F2}%)");

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

        // Helper methods moved to SlotEvaluationService
        private static double CalculateWeight(double expectedRtp, double target)
        {
            double diff = Math.Abs(expectedRtp - target);
            return 1.0 / (diff + 0.01);
        }

        private static ReelSet ChooseWeighted(GameConfig config, List<ReelSet> sets)
        {
            double rtpTarget = config.RtpTarget;
            if (!sets.Any()) return null;
            return sets[_rng.Next(sets.Count)];
        }

        // All evaluation methods moved to SlotEvaluationService

        public static double GetActualRtp() => _totalBet == 0 ? 0 : _totalWin / _totalBet;
        public static double GetActualHitRate() => spinCounter == 0 ? 0 : (double)_hitCount / spinCounter;

        // Reset all static variables to start fresh
        public static void ResetAllStats()
        {
            spinCounter = 0;
            _spinsAboveTarget = 0;
            _spinsBelowTarget = 0;
            _freeSpinsRemaining = 0;
            _freeSpinsAwarded = 0;
            _totalFreeSpinsAwarded = 0;
            _totalBonusesTriggered = 0;
            _totalBet = 0;
            _totalWin = 0;
            _hitCount = 0;
            _lastBonusSpin = -100;
            _rtpMomentum = 0;
            _lastRtp = 0;
            _isSimulationMode = false;
            Console.WriteLine("All stats reset successfully");
        }
    }
}