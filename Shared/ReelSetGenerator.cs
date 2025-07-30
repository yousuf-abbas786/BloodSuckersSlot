using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared
{
    public static class ReelSetGenerator
    {
        public static List<ReelSet> GenerateRandomReelSets(int count = 50)
        {
            var sets = new List<ReelSet>();
            var rng = new Random();

            for (int i = 0; i < count; i++)
            {
                var reels = new List<List<string>>();
                var symbolWeights = new Dictionary<string, int>
                {
                    ["SYM0"] = 3,  // Scatter
                    ["SYM1"] = 3,  // Wild
                    ["SYM2"] = 4,  // Bonus
                    ["SYM3"] = 15,
                    ["SYM4"] = 15,
                    ["SYM5"] = 15,
                    ["SYM6"] = 15,
                    ["SYM7"] = 15,
                    ["SYM8"] = 15,
                    ["SYM9"] = 15,
                    ["SYM10"] = 15
                };

                string tag;
                if (i < count * 0.3)
                {
                    tag = "LowRtp";
                    symbolWeights["SYM3"] = 2;
                    symbolWeights["SYM4"] = 2;
                    symbolWeights["SYM5"] = 2;
                    symbolWeights["SYM6"] = 3;
                    symbolWeights["SYM7"] = 4;
                    symbolWeights["SYM1"] = 0;
                    symbolWeights["SYM0"] = 1;
                    symbolWeights["SYM2"] = 8; // Increased from 1 to 8 for more bonus triggers
                    symbolWeights["SYM8"] = 35;
                    symbolWeights["SYM9"] = 35;
                    symbolWeights["SYM10"] = 35;
                }
                else if (i < count * 0.6)
                {
                    tag = "MidRtp";
                    symbolWeights["SYM3"] = 10;
                    symbolWeights["SYM4"] = 10;
                    symbolWeights["SYM5"] = 12;
                    symbolWeights["SYM6"] = 12;
                    symbolWeights["SYM1"] = 2;
                    symbolWeights["SYM0"] = 2;
                    symbolWeights["SYM2"] = 12; // Increased from default 4 to 12 for more bonus triggers
                    symbolWeights["SYM8"] = 18;
                    symbolWeights["SYM9"] = 18;
                    symbolWeights["SYM10"] = 18;
                }
                else
                {
                    tag = "HighRtp";
                    symbolWeights["SYM3"] = 100;
                    symbolWeights["SYM4"] = 80;
                    symbolWeights["SYM5"] = 70;
                    symbolWeights["SYM6"] = 60;
                    symbolWeights["SYM0"] = 30;
                    symbolWeights["SYM2"] = 35; // Increased from 25 to 35 for more bonus triggers
                    symbolWeights["SYM1"] = 15;
                }

                var weightedSymbols = symbolWeights
                    .SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value))
                    .ToList();

                // SHUFFLE the weighted symbols for better randomness
                for (int shuffleIndex = weightedSymbols.Count - 1; shuffleIndex > 0; shuffleIndex--)
                {
                    int j = rng.Next(shuffleIndex + 1);
                    var temp = weightedSymbols[shuffleIndex];
                    weightedSymbols[shuffleIndex] = weightedSymbols[j];
                    weightedSymbols[j] = temp;
                }

                // 10% of high RTP sets: force visible area to be a guaranteed win
                if (tag == "HighRtp" && rng.NextDouble() < 0.10)
                {
                    var forcedReels = new List<List<string>>();
                    for (int col = 0; col < 5; col++)
                    {
                        var strip = new List<string>();
                        for (int row = 0; row < 20; row++)
                        {
                            if (row < 3)
                            {
                                int winType = rng.Next(3);
                                if (winType == 0)
                                    strip.Add("SYM3");
                                else if (winType == 1)
                                    strip.Add("SYM1");
                                else
                                    strip.Add("SYM0");
                            }
                            else
                            {
                                strip.Add(weightedSymbols[rng.Next(weightedSymbols.Count)]);
                            }
                        }
                        forcedReels.Add(strip);
                    }
                    sets.Add(new ReelSet
                    {
                        Name = $"{tag}Set_{i + 1}_GUARANTEEDWIN",
                        Reels = forcedReels
                    });
                    continue;
                }

                for (int col = 0; col < 5; col++)
                {
                    var strip = new List<string>();
                    int scatterCount = 0;
                    int wildCount = 0;

                    for (int row = 0; row < 20; row++)
                    {
                        string chosen;
                        do
                        {
                            chosen = weightedSymbols[rng.Next(weightedSymbols.Count)];
                            if (chosen == "SYM0" && scatterCount >= 3) continue;
                            if (chosen == "SYM1" && wildCount >= 2) continue;
                            break;
                        } while (true);
                        if (chosen == "SYM0") scatterCount++;
                        if (chosen == "SYM1") wildCount++;
                        // Reduced visible area bias for more realistic distribution
                        if (row < 3 && rng.NextDouble() < 0.20)
                        {
                            if (tag == "HighRtp")
                                chosen = new[] { "SYM3", "SYM4", "SYM5", "SYM6", "SYM1", "SYM0" }[rng.Next(6)];
                            else if (tag == "LowRtp")
                                chosen = new[] { "SYM8", "SYM9", "SYM10", "SYM7", "SYM6" }[rng.Next(5)];
                        }
                        strip.Add(chosen);
                    }
                    reels.Add(strip);
                }
                var set = new ReelSet
                {
                    Name = $"{tag}Set_{i + 1}",
                    Reels = reels
                };
                sets.Add(set);
            }
            return sets;
        }

        public static (double expectedRtp, double estimatedHitRate) MonteCarloSimulate(
            ReelSet set,
            List<int[]> paylines,
            int spins,
            int betAmount,
            GameConfig config,
            int level = 1,
            Func<string[][], List<int[]>, int, (bool, double)> bonusTriggerAndWin = null)
        {
            var rng = new Random();
            double totalWin = 0;
            int winCount = 0;
            int totalSimulatedFreeSpins = 0;
            int maxSimulatedFreeSpins = 100; // Limit to prevent infinite loops
            var freeSpinQueue = new Queue<int>();

            // Use symbol configurations from GameConfig instead of hardcoding
            var symbolConfigs = config.Symbols;

            for (int spin = 0; spin < spins; spin++)
            {
                bool isFreeSpin = freeSpinQueue.Count > 0;
                if (isFreeSpin)
                {
                    freeSpinQueue.Dequeue();
                    totalSimulatedFreeSpins++;
                }

                var grid = SpinReels(set.Reels, rng);
                double lineWin = EvaluatePaylines(grid, paylines, symbolConfigs);
                double wildWin = EvaluateWildLineWins(grid, paylines, symbolConfigs);
                double scatterWin = EvaluateScatters(grid, betAmount, out int scatterCount, symbolConfigs);

                double totalSpinWin = (lineWin + wildWin) * (isFreeSpin ? 3 : 1) + scatterWin;

                // Handle bonus games
                if (bonusTriggerAndWin != null)
                {
                    var (bonusTriggered, bonusWin) = bonusTriggerAndWin(grid, paylines, betAmount);
                    if (bonusTriggered)
                        totalSpinWin += bonusWin;
                }

                if (totalSpinWin > 0) winCount++;
                totalWin += totalSpinWin;

                if (scatterCount >= 3 && totalSimulatedFreeSpins + 10 <= maxSimulatedFreeSpins)
                    freeSpinQueue.Enqueue(10);

                totalSimulatedFreeSpins++;
            }

            double expectedRtp = totalWin / (spins * betAmount);
            double estimatedHitRate = (double)winCount / spins;
            return (expectedRtp, estimatedHitRate);
        }

        // Helper methods for simulation - Updated to use same logic as SlotEngine
        private static string[][] SpinReels(List<List<string>> reels, Random rng)
        {
            var result = new string[5][];
            for (int col = 0; col < 5; col++)
            {
                result[col] = new string[3];
                
                // Pick a random start position (like real slot reels)
                int startPos = rng.Next(reels[col].Count);
                
                // Take the next 3 symbols from that position (wrapping if needed)
                for (int row = 0; row < 3; row++)
                {
                    int pos = (startPos + row) % reels[col].Count;
                    result[col][row] = reels[col][pos];
                }
            }
            return result;
        }

        private static double EvaluatePaylines(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            double win = 0;
            var counted = new HashSet<string>();

            foreach (var line in paylines)
            {
                string baseSymbol = null;
                int matchCount = 0;
                bool wildUsed = false;
                var tempPositions = new List<(int col, int row)>();

                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    
                    // Skip if symbol is not in configuration
                    if (!symbolConfigs.ContainsKey(symbol))
                        break;
                        
                    bool isWild = symbolConfigs[symbol].IsWild;

                    if (col == 0)
                    {
                        if (isWild || symbolConfigs[symbol].IsScatter || symbolConfigs[symbol].IsBonus)
                            break;

                        baseSymbol = symbol;
                        matchCount = 1;
                        tempPositions.Add((col, line[col]));
                    }
                    else
                    {
                        if (symbol == baseSymbol || (isWild && !symbolConfigs[baseSymbol].IsScatter && !symbolConfigs[baseSymbol].IsBonus))
                        {
                            if (isWild) wildUsed = true;
                            matchCount++;
                            tempPositions.Add((col, line[col]));
                        }
                        else break;
                    }
                }

                if (matchCount >= 3 && symbolConfigs.ContainsKey(baseSymbol) && symbolConfigs[baseSymbol].Payouts.TryGetValue(matchCount, out double basePayout))
                {
                    string key = $"{baseSymbol}-{matchCount}-{string.Join(",", line)}";

                    // Check if we already have a higher paying combination for this symbol
                    var existingKeys = counted.Where(k => k.StartsWith($"{baseSymbol}-")).ToList();
                    bool hasHigherPayout = false;
                    
                    foreach (var existingKey in existingKeys)
                    {
                        var parts = existingKey.Split('-');
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int existingCount))
                        {
                            if (existingCount >= matchCount)
                            {
                                hasHigherPayout = true;
                                break;
                            }
                        }
                    }
                    
                    if (!hasHigherPayout)
                    {
                        // Remove any existing lower paying combinations for this symbol
                        counted.RemoveWhere(k => k.StartsWith($"{baseSymbol}-"));
                        
                        // Symbol payouts are static (don't scale with level)
                        double payout = basePayout;
                        win += payout;
                        counted.Add(key);
                    }
                }
            }

            return win;
        }

        private static double EvaluateWildLineWins(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            double wildWin = 0;

            foreach (var line in paylines)
            {
                int count = 0;
                var positions = new List<(int col, int row)>();
                
                for (int col = 0; col < 5; col++)
                {
                    int row = line[col];
                    string symbol = grid[col][row];

                    if (symbol == "SYM1")
                    {
                        count++;
                        positions.Add((col, row));
                    }
                    else
                        break;
                }

                if (count >= 2 && symbolConfigs.ContainsKey("SYM1") && symbolConfigs["SYM1"].Payouts.TryGetValue(count, out double basePayout))
                {
                    // Symbol payouts are static (don't scale with level)
                    double payout = basePayout;
                    wildWin += payout;
                }
            }

            return wildWin;
        }

        private static double EvaluateScatters(string[][] grid, int betAmount, out int scatterCount, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            scatterCount = grid.SelectMany(col => col)
                               .Count(sym => symbolConfigs.ContainsKey(sym) && symbolConfigs[sym].IsScatter);

            double multiplier = 0;
            int freeSpinsAwarded = 0;

            // According to game rules
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
            return scatterWin;
        }
    }
} 