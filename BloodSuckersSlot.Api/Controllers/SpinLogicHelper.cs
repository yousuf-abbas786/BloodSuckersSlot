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
                
                // Pick a random start position (like real slot reels)
                int startPos = _rng.Next(reels[col].Count);
                
                // Take the next 3 symbols from that position (wrapping if needed)
                for (int row = 0; row < 3; row++)
                {
                    int pos = (startPos + row) % reels[col].Count;
                    result[col][row] = reels[col][pos];
                }
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

            for (int paylineIndex = 0; paylineIndex < paylines.Count; paylineIndex++)
            {
                var line = paylines[paylineIndex];
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
                        
                        // Create full payline path (all 5 positions)
                        var fullPaylinePath = new List<Position>();
                        for (int col = 0; col < 5; col++)
                        {
                            fullPaylinePath.Add(new Position { Col = col, Row = line[col] });
                        }
                        
                        // Create winning line with proper data
                        winningLines.Add(new WinningLine
                        {
                            Positions = new List<Position>(tempPositions),
                            Symbol = baseSymbol,
                            Count = matchCount,
                            WinAmount = payout,
                            PaylineType = "line",
                            PaylineIndex = paylineIndex,
                            FullPaylinePath = fullPaylinePath
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

            for (int paylineIndex = 0; paylineIndex < paylines.Count; paylineIndex++)
            {
                var line = paylines[paylineIndex];
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
                    
                    // Create full payline path (all 5 positions)
                    var fullPaylinePath = new List<Position>();
                    for (int col = 0; col < 5; col++)
                    {
                        fullPaylinePath.Add(new Position { Col = col, Row = line[col] });
                    }
                    
                    // Create winning line
                    winningLines.Add(new WinningLine
                    {
                        Positions = positions,
                        Symbol = "SYM1",
                        Count = count,
                        WinAmount = payout,
                        PaylineType = "wild",
                        PaylineIndex = paylineIndex,
                        FullPaylinePath = fullPaylinePath
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

            // DEBUG: Add detailed scatter detection logging
            Console.WriteLine($"DEBUG: Scatter detection - Grid symbols: [{string.Join(", ", grid.SelectMany(col => col))}]");
            Console.WriteLine($"DEBUG: Scatter count: {scatterCount}");
            Console.WriteLine($"DEBUG: IsFreeSpin: {isFreeSpin}");
            Console.WriteLine($"DEBUG: FreeSpinsRemaining: {_freeSpinsRemaining}");
            Console.WriteLine($"DEBUG: FreeSpinsAwarded: {_freeSpinsAwarded}");
            Console.WriteLine($"DEBUG: TotalFreeSpinsAwarded: {_totalFreeSpinsAwarded}");

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
                    _freeSpinsRemaining += freeSpinsAwarded; // Award free spins directly
                    _freeSpinsAwarded += freeSpinsAwarded; // Track free spins awarded in current session
                    _totalFreeSpinsAwarded += freeSpinsAwarded; // Track total free spins awarded
                    Console.WriteLine($"Free Spins Triggered! SYM0 x{scatterCount} => +{freeSpinsAwarded} Free Spins");
                }
                else if (freeSpinsAwarded > 0 && isFreeSpin)
                {
                    Console.WriteLine($"DEBUG: Free spins not awarded during free spin (this is correct behavior)");
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
                var bonusPositions = new List<Position>();
                
                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    if (symbol == "SYM2") // Bonus symbol
                    {
                        count++;
                        bonusPositions.Add(new Position { Col = col, Row = line[col] });
                    }
                    else break;
                }

                if (count >= 3) // Need 3 or more bonus symbols to trigger
                {
                    // Check cooldown period to prevent too frequent bonus triggers
                    if (_isSimulationMode || spinCounter - _lastBonusSpin >= 50) // 50 spin cooldown
                    {
                        if (!_isSimulationMode)
                        {
                            _lastBonusSpin = spinCounter;
                            _totalBonusesTriggered++; // Track total bonuses triggered
                        }

                        bonusLog = $"🎰 BONUS TRIGGERED! Coffin symbols x{count} on payline [{string.Join(",", line)}] - Coffin Selection Bonus Game!";
                        Console.WriteLine($"🎰 BONUS TRIGGERED: {count} coffin symbols on payline [{string.Join(",", line)}]");
                        Console.WriteLine($"  Bonus positions: {string.Join(", ", bonusPositions.Select(p => $"({p.Col},{p.Row})"))}");
                        
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"🎰 BONUS BLOCKED: Too soon since last bonus (spin {spinCounter - _lastBonusSpin} ago)");
                    }
                }
            }
            return false;
        }

        private static double SimulateBonusGame(GameConfig config, double currentRtpBeforeSpin)
        {
            // BloodSuckers Bonus Game: Coffin Selection
            // The actual game has a coffin selection bonus where players pick coffins to reveal prizes
            // Each coffin contains different multipliers or cash prizes
            
            Console.WriteLine("🎰 BONUS GAME TRIGGERED: Coffin Selection!");
            
            // Simulate coffin selection bonus game
            int coffinSelections = 3; // Player gets 3 coffin picks
            double totalBonusWin = 0;
            var bonusDetails = new List<string>();
            
            for (int pick = 1; pick <= coffinSelections; pick++)
            {
                // Different coffin types with different prize distributions
                double coffinWin = 0;
                string coffinType = "";
                
                // Randomly determine coffin type and prize
                double randomValue = _rng.NextDouble();
                
                if (randomValue < 0.4) // 40% chance - Small prize coffin
                {
                    coffinWin = 5 + _rng.NextDouble() * 10; // 5-15 coins
                    coffinType = "Small Prize Coffin";
                }
                else if (randomValue < 0.7) // 30% chance - Medium prize coffin
                {
                    coffinWin = 15 + _rng.NextDouble() * 20; // 15-35 coins
                    coffinType = "Medium Prize Coffin";
                }
                else if (randomValue < 0.9) // 20% chance - Large prize coffin
                {
                    coffinWin = 35 + _rng.NextDouble() * 30; // 35-65 coins
                    coffinType = "Large Prize Coffin";
                }
                else // 10% chance - Jackpot coffin
                {
                    coffinWin = 65 + _rng.NextDouble() * 50; // 65-115 coins
                    coffinType = "Jackpot Coffin";
                }
                
                // Apply RTP scaling to balance the game
                double rtpDeficit = Math.Max(0, config.RtpTarget - currentRtpBeforeSpin);
                double rtpMultiplier = 1.0 + (rtpDeficit * 0.5); // Scale up when RTP is low
                coffinWin *= rtpMultiplier;
                
                totalBonusWin += coffinWin;
                bonusDetails.Add($"Pick {pick}: {coffinType} = {coffinWin:F1} coins");
                
                Console.WriteLine($"  Coffin {pick}: {coffinType} - {coffinWin:F1} coins");
            }
            
            // Cap the maximum bonus win to prevent excessive payouts
            double maxBonusWin = 150; // Cap at 150 coins
            totalBonusWin = Math.Min(totalBonusWin, maxBonusWin);
            
            Console.WriteLine($"🎰 BONUS GAME COMPLETE: Total Win = {totalBonusWin:F1} coins");
            Console.WriteLine($"  Details: {string.Join(" | ", bonusDetails)}");
            
            return totalBonusWin;
        }

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