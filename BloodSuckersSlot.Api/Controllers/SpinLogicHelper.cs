using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared;
using BloodSuckersSlot.Api.Models;
using System.Threading;

namespace BloodSuckersSlot.Api.Controllers
{
    public static class SpinLogicHelper
    {
        private static readonly Random _rng = new();
        private static int spinCounter = 0;
        private static int _spinsAboveTarget = 0;
        private static int _spinsBelowTarget = 0;
        private static int _freeSpinsRemaining = 0;
        private static int _freeSpinsAwarded = 0; // FIXED: Add missing free spin tracking
        private static int _totalFreeSpinsAwarded = 0; // FIXED: Track total free spins awarded
        private static int _totalBonusesTriggered = 0; // FIXED: Track total bonuses triggered
        private static int _freeSpinRetryCount = 0;
        private const int MaxFreeSpinRetries = 10;
        private const int MaxFreeSpinsPerSession = 50; // FIXED: Add free spin session limit
        private static readonly bool _freeSpinRtpGuardEnabled = true; // FIXED: Add free spin RTP guard flag
        private static double _totalBet = 0;
        private static double _totalWin = 0;
        private static int _hitCount = 0;
        private static int _lastBonusSpin = -100;
        private static int _rtpMomentum = 0;
        private static double _lastRtp = 0;
        private static bool _isSimulationMode = false;

        // FIXED: Remove hardcoded symbol configs - use GameConfig.Symbols like original SlotEngine

        public static (SpinResult Result, string[][] Grid, ReelSet ChosenSet, List<WinningLine> WinningLines) SpinWithReelSets(GameConfig config, int betAmount, List<ReelSet> reelSetsFromDb)
        {
            List<ReelSet> healthySets = new();
            bool isFreeSpin = _freeSpinsRemaining > 0;
            double currentRtpBeforeSpin = GetActualRtp();

            // Correction logic (same as original)
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

            if (isFreeSpin && _freeSpinRtpGuardEnabled && currentRtpBeforeSpin > config.RtpTarget * 1.15)
            {
                _freeSpinRetryCount++;
                if (_freeSpinRetryCount > MaxFreeSpinRetries)
                {
                    Console.WriteLine("[Free Spin Delay] Max retries exceeded. Forcing execution.");
                }
                else
                {
                    if (_freeSpinRetryCount % 3 == 0)
                        Console.WriteLine($"[Free Spin Delay] Retry {_freeSpinRetryCount} — RTP too high: {currentRtpBeforeSpin:F2}");
                    Thread.Sleep(200);
                    return (null, null, null, null);
                }
            }
            else
            {
                _freeSpinRetryCount = 0;
            }

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

            var grid = SpinReels(chosenSet.Reels);
            var winningLines = new List<WinningLine>();

            // Evaluate wins and collect winning lines - FIXED: Use proper symbol configs
            var lineWin = EvaluatePaylinesWithLines(grid, config.Paylines, config.Symbols, out var lineWinningLines);
            var wildWin = EvaluateWildLineWinsWithLines(grid, config.Paylines, config.Symbols, out var wildWinningLines);
            var scatterWin = EvaluateScattersWithLines(grid, config.Symbols, isFreeSpin, out var scatterWinningLines, out var scatterCount, betAmount);

            // FIXED: Apply free spin multiplier like original SlotEngine
            var totalWin = (lineWin * (isFreeSpin ? 3 : 1)) + wildWin + scatterWin;

            // Combine all winning lines
            winningLines.AddRange(lineWinningLines);
            winningLines.AddRange(wildWinningLines);
            winningLines.AddRange(scatterWinningLines);

            // Generate SVG paths for winning lines
            foreach (var line in winningLines)
            {
                line.SvgPath = CreateSvgPath(line.Positions);
            }

            var bonusWin = 0.0;
            var bonusLog = "";

            if (CheckBonusTrigger(grid, config.Paylines, config.Symbols, scatterCount, ref bonusLog))
            {
                bonusWin = SimulateBonusGame(config, currentRtpBeforeSpin);
                totalWin += bonusWin;
            }

            // FIXED: Apply win caps like original SlotEngine
            double maxMultiplier = 75.0; // Cap win to 75x of bet
            totalWin = Math.Min(totalWin, betAmount * maxMultiplier);

            if (spinCounter < 25 && totalWin > betAmount * 15)
            {
                Console.WriteLine($"[Early Spin Control] Dampened spin win from {totalWin} to {betAmount * 15}");
                totalWin = betAmount * 15;  // Damp early big wins
            }

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

        private static string[][] SpinReels(List<List<string>> reels)
        {
            var result = new string[5][];
            for (int col = 0; col < 5; col++)
            {
                result[col] = new string[3];
                for (int row = 0; row < 3; row++)
                    result[col][row] = reels[col][_rng.Next(reels[col].Count)];
            }
            return result;
        }

        // FIXED: Properly copied evaluation logic from original SlotEngine
        private static double EvaluatePaylinesWithLines(
            string[][] grid,
            List<int[]> paylines,
            Dictionary<string, SymbolConfig> symbolConfigs,
            out List<WinningLine> winningLines)
        {
            double win = 0;
            var counted = new HashSet<string>();
            winningLines = new List<WinningLine>();

            foreach (var line in paylines)
            {
                string baseSymbol = null;
                int matchCount = 0;
                bool wildUsed = false;
                var tempPositions = new List<Position>();

                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    bool isWild = symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsWild;

                    if (col == 0)
                    {
                        if (isWild || (symbolConfigs.ContainsKey(symbol) && (symbolConfigs[symbol].IsScatter || symbolConfigs[symbol].IsBonus)))
                            break;

                        baseSymbol = symbol;
                        matchCount = 1;
                        tempPositions.Add(new Position { Col = col, Row = line[col] });
                    }
                    else
                    {
                        if (symbol == baseSymbol || (isWild && symbolConfigs.ContainsKey(baseSymbol) && !symbolConfigs[baseSymbol].IsScatter && !symbolConfigs[baseSymbol].IsBonus))
                        {
                            if (isWild) wildUsed = true;
                            matchCount++;
                            tempPositions.Add(new Position { Col = col, Row = line[col] });
                        }
                        else break;
                    }
                }

                if (matchCount >= 3 && symbolConfigs.ContainsKey(baseSymbol) && symbolConfigs[baseSymbol].Payouts.TryGetValue(matchCount, out double payout))
                {
                    string key = $"{baseSymbol}-{matchCount}-{string.Join(",", line)}";

                    if (counted.Any(k => k.StartsWith($"{baseSymbol}-")))
                        continue;

                    if (!counted.Contains(key))
                    {
                        win += payout;
                        counted.Add(key);
                        
                        Console.WriteLine($"EvaluatePaylinesWithLines: Found winning line - Symbol: {baseSymbol}, Count: {matchCount}, Win: {payout}");
                        
                        // Create winning line with proper data
                        winningLines.Add(new WinningLine
                        {
                            Positions = new List<Position>(tempPositions),
                            Symbol = baseSymbol,
                            Count = matchCount,
                            WinAmount = payout,
                            PaylineType = "line"
                        });
                    }
                }
            }

            return win;
        }

        private static double EvaluateWildLineWinsWithLines(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs, out List<WinningLine> winningLines)
        {
            double wildWin = 0;
            winningLines = new List<WinningLine>();

            foreach (var line in paylines)
            {
                int count = 0;
                var positions = new List<Position>();
                
                for (int col = 0; col < 5; col++)
                {
                    int row = line[col];
                    string symbol = grid[col][row];

                    if (symbol == "SYM1")
                    {
                        count++;
                        positions.Add(new Position { Col = col, Row = row });
                    }
                    else
                        break;
                }

                if (count >= 2 && symbolConfigs.ContainsKey("SYM1") && symbolConfigs["SYM1"].Payouts.TryGetValue(count, out double payout))
                {
                    wildWin += payout;
                    
                    Console.WriteLine($"EvaluateWildLineWinsWithLines: Found wild win - Count: {count}, Win: {payout}");
                    
                    // Create winning line
                    winningLines.Add(new WinningLine
                    {
                        Positions = positions,
                        Symbol = "SYM1",
                        Count = count,
                        WinAmount = payout,
                        PaylineType = "wild"
                    });
                }
            }

            return wildWin;
        }

        private static double EvaluateScattersWithLines(string[][] grid, Dictionary<string, SymbolConfig> symbolConfigs, bool isFreeSpin, out List<WinningLine> winningLines, out int scatterCount, int betAmount)
        {
            winningLines = new List<WinningLine>();
            scatterCount = grid.SelectMany(col => col)
                .Count(symbol => symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsScatter);

            if (scatterCount >= 2) // FIXED: Changed from >= 3 to >= 2 to match original logic
            {
                var scatterPositions = new List<Position>();
                
                // Find scatter positions
                for (int col = 0; col < 5; col++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        string symbol = grid[col][row];
                        if (symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsScatter)
                        {
                            scatterPositions.Add(new Position { Col = col, Row = row });
                        }
                    }
                }

                // FIXED: Use original hardcoded scatter payout logic from SlotEngine
                double multiplier = 0;
                int freeSpinsAwarded = 0;
                
                switch (scatterCount)
                {
                    case 2:
                        multiplier = 2;
                        break;
                    case 3:
                        multiplier = 4;
                        freeSpinsAwarded = 10;
                        break;
                    case 4:
                        multiplier = 25;
                        freeSpinsAwarded = 10;
                        break;
                    case 5:
                        multiplier = 100;
                        freeSpinsAwarded = 10;
                        break;
                }

                double scatterWin = multiplier * betAmount;
                
                Console.WriteLine($"EvaluateScattersWithLines: Found scatter win - Count: {scatterCount}, Multiplier: {multiplier}, Win: {scatterWin}");
                
                // FIXED: Add free spin handling like original SlotEngine
                if (freeSpinsAwarded > 0 && !isFreeSpin)
                {
                    int remainingAllowance = MaxFreeSpinsPerSession - _freeSpinsAwarded;
                    int toAward = Math.Min(remainingAllowance, freeSpinsAwarded);
                    if (toAward > 0)
                    {
                        _freeSpinsRemaining += toAward;
                        _freeSpinsAwarded += toAward;
                        _totalFreeSpinsAwarded += toAward; // Track total free spins awarded
                        Console.WriteLine($"Free Spins Triggered! SYM0 x{scatterCount} => +{toAward} Free Spins");
                    }
                }
                
                // Create winning line for scatters
                winningLines.Add(new WinningLine
                {
                    Positions = scatterPositions,
                    Symbol = "SCATTER",
                    Count = scatterCount,
                    WinAmount = scatterWin,
                    PaylineType = "scatter"
                });

                return scatterWin;
            }

            return 0;
        }

        private static string CreateSvgPath(List<Position> positions)
        {
            if (positions.Count == 0) 
            {
                Console.WriteLine("CreateSvgPath: No positions provided");
                return "";
            }
            
            var path = new StringBuilder();
            bool first = true;
            
            Console.WriteLine($"CreateSvgPath: Processing {positions.Count} positions");
            
            foreach (var pos in positions)
            {
                // Convert grid position to SVG coordinates
                int x = pos.Col * 100 + 50; // 100px per column, center at 50
                int y = pos.Row * 60 + 30;  // 60px per row, center at 30
                
                Console.WriteLine($"CreateSvgPath: Position ({pos.Col},{pos.Row}) -> SVG ({x},{y})");
                
                if (first)
                {
                    path.Append($"M {x} {y}");
                    first = false;
                }
                else
                {
                    path.Append($" L {x} {y}");
                }
            }
            
            var result = path.ToString();
            Console.WriteLine($"CreateSvgPath: Generated path: {result}");
            return result;
        }

        private static bool CheckBonusTrigger(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs, int scatterCount, ref string bonusLog)
        {
            foreach (var line in paylines)
            {
                int count = 0;
                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    if (symbol == "SYM2") count++;
                    else break;

                    if (count >= 3)
                    {
                        // FIXED: Add proper bonus tracking like original SlotEngine
                        if (_isSimulationMode || spinCounter - _lastBonusSpin >= 50) // Reduced from 250 to 50 spins
                        {
                            if (!_isSimulationMode)
                            {
                                _lastBonusSpin = spinCounter;
                                _totalBonusesTriggered++; // Track total bonuses triggered
                            }

                            bonusLog = $"Bonus Triggered! SYM2 x{count} on Payline: [{string.Join(",", line)}]";
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static double SimulateBonusGame(GameConfig config, double currentRtpBeforeSpin)
        {
            double rtpDeficit = Math.Max(0, config.RtpTarget - currentRtpBeforeSpin);
            double baseBonus = 10 + Math.Min(rtpDeficit * 20, 18);  // cap the scaling
            double bonusWin = baseBonus + _rng.NextDouble() * 25;   // Was 30
            double maxBonusWin = 40;                                // Was 45
            bonusWin = Math.Min(bonusWin, maxBonusWin);

            Console.WriteLine($"Bonus Game Win: {bonusWin:F1} coins");
            return bonusWin;
        }

        public static double GetActualRtp() => _totalBet == 0 ? 0 : _totalWin / _totalBet;
        public static double GetActualHitRate() => spinCounter == 0 ? 0 : (double)_hitCount / spinCounter;
    }
} 