using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared
{
    public static class ReelSetGenerator
    {

        public static (double expectedRtp, double estimatedHitRate) MonteCarloSimulate(
            ReelSet set,
            List<int[]> paylines,
            int spins,
            int betAmount,
            GameConfig config,
            int level = 1,
            Func<string[][], List<int[]>, int, (bool, double)> bonusTriggerAndWin = null)
        {
            var rng = new Random(Guid.NewGuid().GetHashCode());
            double totalWin = 0;
            int winCount = 0;
            int totalSimulatedFreeSpins = 0;
            int maxSimulatedFreeSpins = 100;
            var freeSpinQueue = new Queue<int>();

            var symbolConfigs = config.Symbols;

            // Pre-allocate and reuse arrays for better performance
            var grid = new string[5][];
            for (int col = 0; col < 5; col++)
            {
                grid[col] = new string[3];
            }



            // Cache bet amount as double to avoid repeated int-to-double conversions
            double betAmountDouble = betAmount;

            // Pre-convert data structures for maximum performance
            var reelArrays = new string[5][];
            var reelLengths = new int[5];
            for (int i = 0; i < 5; i++)
            {
                reelArrays[i] = set.Reels[i].ToArray();
                reelLengths[i] = reelArrays[i].Length;
            }
            
            // Pre-convert paylines to arrays
            var paylineArrays = new int[paylines.Count][];
            for (int i = 0; i < paylines.Count; i++)
            {
                paylineArrays[i] = paylines[i].ToArray();
            }

            // Optimized simulation loop - use EXACT same evaluation logic as API but with performance optimizations
            for (int spin = 0; spin < spins; spin++)
            {
                bool isFreeSpin = freeSpinQueue.Count > 0;
                if (isFreeSpin)
                {
                    freeSpinQueue.Dequeue();
                    totalSimulatedFreeSpins++;
                }

                // Optimized reel spinning - inline for performance but SAME logic as API
                for (int col = 0; col < 5; col++)
                {
                    int startPos = rng.Next(reelLengths[col]);
                    var reel = reelArrays[col];
                    grid[col][0] = reel[startPos];
                    grid[col][1] = reel[(startPos + 1) % reelLengths[col]];
                    grid[col][2] = reel[(startPos + 2) % reelLengths[col]];
                }

                // Use the EXACT SAME evaluation logic as the API for consistency - no shortcuts!
                double lineWin = SlotEvaluationService.EvaluatePaylinesOptimized(grid, paylines, symbolConfigs);
                double wildWin = SlotEvaluationService.EvaluateWildLineWinsOptimized(grid, paylines, symbolConfigs);
                double scatterWin = SlotEvaluationService.EvaluateScattersOptimized(grid, betAmount, out int scatterCount, symbolConfigs);
                
                double totalSpinWin = lineWin + wildWin + scatterWin;

                // Apply free spin tripling
                if (isFreeSpin) totalSpinWin *= 3;

                // Handle bonus games (if any)
                if (bonusTriggerAndWin != null)
                {
                    var (bonusTriggered, bonusWin) = bonusTriggerAndWin(grid, paylines, betAmount);
                    if (bonusTriggered) totalSpinWin += bonusWin;
                }

                if (totalSpinWin > 0) winCount++;
                totalWin += totalSpinWin;

                // Free spin triggering
                if (scatterCount >= 3 && totalSimulatedFreeSpins + 10 <= maxSimulatedFreeSpins)
                    freeSpinQueue.Enqueue(10);

                totalSimulatedFreeSpins++;
            }

            double expectedRtp = totalWin / (spins * betAmountDouble);
            double estimatedHitRate = (double)winCount / spins;
            return (expectedRtp, estimatedHitRate);
        }



        // All helper methods moved to SlotEvaluationService
    }
}