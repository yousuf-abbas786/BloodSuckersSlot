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
                    symbolWeights["SYM2"] = 1;
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
                    symbolWeights["SYM2"] = 25;
                    symbolWeights["SYM1"] = 15;
                }

                var weightedSymbols = symbolWeights
                    .SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value))
                    .ToList();

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
                        if (row < 3 && rng.NextDouble() < 0.8)
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
            Func<string[][], List<int[]>, int, (bool, double)> bonusTriggerAndWin = null)
        {
            double totalWin = 0;
            int winCount = 0;
            var freeSpinQueue = new Queue<int>();
            int maxMultiplier = 40; // cap per spin
            var rng = new Random();

            for (int i = 0; i < spins; i++)
            {
                var grid = SpinReels(set.Reels, rng);
                double lineWin = EvaluatePaylines(grid, paylines);
                double wildWin = EvaluateWildLineWins(grid, paylines);
                double scatterWin = EvaluateScatters(grid, betAmount, out int scatterCount);

                double bonusWin = 0;
                bool bonusTriggered = false;
                if (bonusTriggerAndWin != null)
                {
                    (bonusTriggered, bonusWin) = bonusTriggerAndWin(grid, paylines, scatterCount);
                }
                else
                {
                    // Default: 5% chance if 3+ scatters
                    if (scatterCount >= 3 && rng.NextDouble() < 0.05)
                        bonusWin = 10 + rng.NextDouble() * 15;
                }

                double spinWin = lineWin + wildWin + scatterWin + bonusWin;
                spinWin = Math.Min(spinWin, betAmount * maxMultiplier);

                if (spinWin > 0) winCount++;
                totalWin += spinWin;

                if (scatterCount >= 3)
                    freeSpinQueue.Enqueue(10);
            }

            int totalSimulatedFreeSpins = 0;
            int maxSimulatedFreeSpins = 10;
            while (freeSpinQueue.Count > 0 && totalSimulatedFreeSpins < maxSimulatedFreeSpins)
            {
                int count = freeSpinQueue.Dequeue();
                for (int j = 0; j < count; j++)
                {
                    var grid = SpinReels(set.Reels, rng);
                    double lineWin = EvaluatePaylines(grid, paylines);
                    double wildWin = EvaluateWildLineWins(grid, paylines);
                    double scatterWin = EvaluateScatters(grid, betAmount, out int scatterCount);

                    double bonusWin = 0;
                    bool bonusTriggered = false;
                    if (bonusTriggerAndWin != null)
                    {
                        (bonusTriggered, bonusWin) = bonusTriggerAndWin(grid, paylines, scatterCount);
                    }
                    else
                    {
                        if (scatterCount >= 3 && rng.NextDouble() < 0.05)
                            bonusWin = 10 + rng.NextDouble() * 15;
                    }

                    double freeSpinWin = (lineWin + wildWin) * 3 + scatterWin + bonusWin;
                    freeSpinWin = Math.Min(freeSpinWin, betAmount * maxMultiplier);

                    if (freeSpinWin > 0) winCount++;
                    totalWin += freeSpinWin;

                    if (scatterCount >= 3 && totalSimulatedFreeSpins + 10 <= maxSimulatedFreeSpins)
                        freeSpinQueue.Enqueue(10);

                    totalSimulatedFreeSpins++;
                }
            }

            double expectedRtp = totalWin / (spins * betAmount);
            double estimatedHitRate = (double)winCount / spins;
            return (expectedRtp, estimatedHitRate);
        }

        // Helper methods for simulation
        private static string[][] SpinReels(List<List<string>> reels, Random rng)
        {
            var result = new string[5][];
            for (int col = 0; col < 5; col++)
            {
                result[col] = new string[3];
                for (int row = 0; row < 3; row++)
                    result[col][row] = reels[col][rng.Next(reels[col].Count)];
            }
            return result;
        }

        private static double EvaluatePaylines(string[][] grid, List<int[]> paylines)
        {
            // Simplified: count any 3+ consecutive same symbol on a payline as a win
            double win = 0;
            foreach (var line in paylines)
            {
                string baseSymbol = grid[0][line[0]];
                int matchCount = 1;
                for (int col = 1; col < 5; col++)
                {
                    if (grid[col][line[col]] == baseSymbol)
                        matchCount++;
                    else
                        break;
                }
                if (matchCount >= 3)
                    win += matchCount; // Placeholder: 1 coin per symbol in match
            }
            return win;
        }

        private static double EvaluateWildLineWins(string[][] grid, List<int[]> paylines)
        {
            // Simplified: count any 2+ wilds on a payline as a win
            double win = 0;
            foreach (var line in paylines)
            {
                int wildCount = 0;
                for (int col = 0; col < 5; col++)
                {
                    if (grid[col][line[col]] == "SYM1")
                        wildCount++;
                    else
                        break;
                }
                if (wildCount >= 2)
                    win += wildCount; // Placeholder: 1 coin per wild
            }
            return win;
        }

        private static double EvaluateScatters(string[][] grid, int betAmount, out int scatterCount)
        {
            scatterCount = grid.SelectMany(col => col).Count(sym => sym == "SYM0");
            double multiplier = 0;
            switch (scatterCount)
            {
                case 2:
                    multiplier = 2;
                    break;
                case 3:
                    multiplier = 4;
                    break;
                case 4:
                    multiplier = 25;
                    break;
                case 5:
                    multiplier = 100;
                    break;
            }
            return multiplier * betAmount;
        }
    }
} 