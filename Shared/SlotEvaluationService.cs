using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Shared
{
    public static class SlotEvaluationService
    {
        private static readonly Random _rng = new();
        
        // OFFICIAL BLOODSUCKERS WILD PAYTABLE
        // Wild-of-a-kind pays from 2 in a row with specific values:
        // 5 wilds: 7500 × line bet, 4 wilds: 2000 × line bet, 3 wilds: 200 × line bet, 2 wilds: 5 × line bet
        private static readonly Dictionary<int, double> WildPaytable = new()
        {
            { 2, 5.0 },
            { 3, 200.0 },
            { 4, 2000.0 },
            { 5, 7500.0 }
        };

        // OFFICIAL BLOODSUCKERS MALFUNCTION RULE
        // Any malfunction voids all pays and plays
        public static bool DetectMalfunction(string[][] grid, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            try
            {
                // Check for invalid symbols in grid
                for (int col = 0; col < 5; col++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        string symbol = grid[col][row];
                        if (string.IsNullOrEmpty(symbol) || !symbolConfigs.ContainsKey(symbol))
                        {
                            Console.WriteLine($"MALFUNCTION DETECTED: Invalid symbol '{symbol}' at position ({col},{row})");
                            return true;
                        }
                    }
                }
                
                // Check for null or invalid symbol configurations
                if (symbolConfigs == null || symbolConfigs.Count == 0)
                {
                    Console.WriteLine("MALFUNCTION DETECTED: Invalid symbol configuration");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MALFUNCTION DETECTED: Exception during grid validation: {ex.Message}");
                return true;
            }
        }

        public static string[][] SpinReels(List<List<string>> reels)
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

        // Optimized version that reuses the grid array for better performance in simulations
        public static void SpinReelsOptimized(List<List<string>> reels, Random rng, string[][] grid)
        {
            for (int col = 0; col < 5; col++)
            {
                // Pick a random start position (like real slot reels)
                int startPos = rng.Next(reels[col].Count);

                // Take the next 3 symbols from that position (wrapping if needed)
                for (int row = 0; row < 3; row++)
                {
                    int pos = (startPos + row) % reels[col].Count;
                    grid[col][row] = reels[col][pos];
                }
            }
        }

        public static double EvaluatePaylinesWithLines(
            string[][] grid,
            List<int[]> paylines,
            Dictionary<string, SymbolConfig> symbolConfigs,
            out List<WinningLine> winningLines)
        {
            double win = 0;
            // Use paylineIndex as key to properly distinguish between different paylines
            // This ensures the "Highest Only Rule" is applied per payline, not across all paylines
            var paylineWins = new Dictionary<int, Dictionary<string, int>>(); // paylineIndex -> symbol -> highest count
            winningLines = new List<WinningLine>();

            for (int paylineIndex = 0; paylineIndex < paylines.Count; paylineIndex++)
            {
                var line = paylines[paylineIndex];
                string baseSymbol = null;
                int matchCount = 0;
                var tempPositions = new List<Position>();

                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    
                    // Skip if symbol is not in configuration
                    if (!symbolConfigs.ContainsKey(symbol))
                        break;
                        
                    bool isWild = symbolConfigs[symbol].IsWild;
                    bool isScatter = symbolConfigs[symbol].IsScatter;
                    bool isBonus = symbolConfigs[symbol].IsBonus;

                    if (col == 0)
                    {
                        if (isWild || isScatter || isBonus)
                            break;

                        baseSymbol = symbol;
                        matchCount = 1;
                        tempPositions.Add(new Position { Col = col, Row = line[col] });
                    }
                    else
                    {
                        // Only allow exact symbol matches, no wild substitution in EvaluatePaylinesWithLines
                        // Wild combinations should be handled exclusively by EvaluateWildLineWinsWithLines
                        if (symbol == baseSymbol)
                        {
                            matchCount++;
                            tempPositions.Add(new Position { Col = col, Row = line[col] });
                        }
                        else 
                        {
                            break;
                        }
                    }
                }

                if (matchCount >= 3 && symbolConfigs.ContainsKey(baseSymbol) && symbolConfigs[baseSymbol].Payouts.TryGetValue(matchCount, out double basePayout))
                {
                    // Use paylineIndex to properly distinguish between different paylines
                    // This ensures each payline is evaluated independently for the "Highest Only Rule"
                    
                    // Check if we already have a higher count for this symbol on this specific payline
                    if (!paylineWins.ContainsKey(paylineIndex))
                        paylineWins[paylineIndex] = new Dictionary<string, int>();
                    
                    if (!paylineWins[paylineIndex].ContainsKey(baseSymbol) || paylineWins[paylineIndex][baseSymbol] < matchCount)
                    {
                        // Update to the higher count for this symbol on this payline
                        paylineWins[paylineIndex][baseSymbol] = matchCount;
                    }
                }
            }

            // Now sum up all the wins across all paylines and create winning lines
            foreach (var paylineEntry in paylineWins)
            {
                int paylineIndex = paylineEntry.Key;
                var payline = paylines[paylineIndex]; // Get the actual payline from the original list
                
                foreach (var symbolEntry in paylineEntry.Value)
                {
                    string symbol = symbolEntry.Key;
                    int count = symbolEntry.Value;
                    
                    if (symbolConfigs[symbol].Payouts.TryGetValue(count, out double payout))
                    {
                        win += payout;
                        
                        // Create winning positions (only the matching positions)
                        var winningPositions = new List<Position>();
                        for (int col = 0; col < count; col++)
                        {
                            winningPositions.Add(new Position { Col = col, Row = payline[col] });
                        }
                        
                        // Create full payline path (all 5 positions)
                        var fullPaylinePath = new List<Position>();
                        for (int col = 0; col < 5; col++)
                        {
                            fullPaylinePath.Add(new Position { Col = col, Row = payline[col] });
                        }
                        
                        // Create winning line with proper data
                        winningLines.Add(new WinningLine
                        {
                            Positions = new List<Position>(winningPositions),
                            Symbol = symbol,
                            Count = count,
                            WinAmount = payout,
                            PaylineType = "line",
                            PaylineIndex = paylineIndex, // Use the actual payline index
                            FullPaylinePath = fullPaylinePath
                        });
                    }
                }
            }

            return win;
        }

        // Optimized version for simulations without winningLines tracking
        public static double EvaluatePaylinesOptimized(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            double win = 0;
            // Use paylineIndex as key to properly distinguish between different paylines - SAME AS MASTER METHOD
            // This ensures the "Highest Only Rule" is applied per payline, not across all paylines
            var paylineWins = new Dictionary<int, Dictionary<string, int>>(); // paylineIndex -> symbol -> highest count

            for (int paylineIndex = 0; paylineIndex < paylines.Count; paylineIndex++)
            {
                var line = paylines[paylineIndex];
                string baseSymbol = null;
                int matchCount = 0;

                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];

                    // Skip if symbol is not in configuration
                    if (!symbolConfigs.ContainsKey(symbol))
                        break;

                    bool isWild = symbolConfigs[symbol].IsWild;
                    bool isScatter = symbolConfigs[symbol].IsScatter;
                    bool isBonus = symbolConfigs[symbol].IsBonus;

                    if (col == 0)
                    {
                        if (isWild || isScatter || isBonus)
                            break;

                        baseSymbol = symbol;
                        matchCount = 1;
                    }
                    else
                    {
                        // Only allow exact symbol matches, no wild substitution in EvaluatePaylinesOptimized
                        // Wild combinations should be handled exclusively by EvaluateWildLineWinsOptimized
                        if (symbol == baseSymbol)
                        {
                            matchCount++;
                        }
                        else 
                        {
                            break;
                        }
                    }
                }

                if (matchCount >= 3 && symbolConfigs.ContainsKey(baseSymbol) && symbolConfigs[baseSymbol].Payouts.TryGetValue(matchCount, out double basePayout))
                {
                    // Use paylineIndex to properly distinguish between different paylines - SAME AS MASTER METHOD
                    // This ensures each payline is evaluated independently for the "Highest Only Rule"
                    
                    // Check if we already have a higher count for this symbol on this specific payline
                    if (!paylineWins.ContainsKey(paylineIndex))
                        paylineWins[paylineIndex] = new Dictionary<string, int>();
                    
                    if (!paylineWins[paylineIndex].ContainsKey(baseSymbol) || paylineWins[paylineIndex][baseSymbol] < matchCount)
                    {
                        // Update to the higher count for this symbol on this payline
                        paylineWins[paylineIndex][baseSymbol] = matchCount;
                    }
                }
            }

            // Now sum up all the wins across all paylines - SAME AS MASTER METHOD
            foreach (var paylineEntry in paylineWins)
            {
                foreach (var symbolEntry in paylineEntry.Value)
                {
                    string symbol = symbolEntry.Key;
                    int count = symbolEntry.Value;

                    if (symbolConfigs[symbol].Payouts.TryGetValue(count, out double payout))
                    {
                        win += payout;
                    }
                }
            }

            return win;
        }

        public static double EvaluateWildLineWinsWithLines(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs, out List<WinningLine> winningLines)
        {
            double wildWin = 0;
            winningLines = new List<WinningLine>();

            for (int paylineIndex = 0; paylineIndex < paylines.Count; paylineIndex++)
            {
                var line = paylines[paylineIndex];
                int wildCount = 0;
                int symbolCount = 0;
                string symbolType = null;
                bool hasSymbols = false;
                var wildPositions = new List<Position>();
                var symbolPositions = new List<Position>();
                
                // Count wilds and symbols separately
                // OFFICIAL BLOODSUCKERS RULE: Wild-only wins must start from leftmost reel (column 0)
                bool wildOnlyStartsFromLeftmost = false;
                int firstWildColumn = -1;
                
                for (int col = 0; col < 5; col++)
                {
                    int row = line[col];
                    string symbol = grid[col][row];

                    if (symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsWild)
                    {
                        if (firstWildColumn == -1)
                        {
                            firstWildColumn = col; // Track the first wild position
                        }
                        wildCount++;
                        wildPositions.Add(new Position { Col = col, Row = row });
                    }
                    else if (symbolConfigs.ContainsKey(symbol) && !symbolConfigs[symbol].IsWild && !symbolConfigs[symbol].IsScatter && !symbolConfigs[symbol].IsBonus)
                    {
                        // Regular symbol
                        if (symbolType == null)
                        {
                            symbolType = symbol;
                            symbolCount = 1;
                            hasSymbols = true;
                        }
                        else if (symbol == symbolType)
                        {
                            symbolCount++;
                        }
                        else
                        {
                            // Different symbol, break the line
                            break;
                        }
                        symbolPositions.Add(new Position { Col = col, Row = row });
                    }
                    else
                    {
                        // Invalid symbol, break the line
                        break;
                    }
                }
                
                // Check if wild-only win starts from leftmost reel
                wildOnlyStartsFromLeftmost = (firstWildColumn == 0);

                // Only process this payline if it actually contains wilds
                if (wildCount == 0)
                    continue; // Skip this payline - no wilds to process

                // Compare wild-only vs symbol+wild wins, take the highest
                double wildOnlyPayout = 0;
                double symbolWithWildPayout = 0;
                var winningPositions = new List<Position>();

                // OFFICIAL BLOODSUCKERS RULE: Wild-of-a-kind pays from 2 in a row using specific wild paytable
                // BUT ONLY if wilds start from the leftmost reel (column 0)
                if (wildCount >= 2 && wildOnlyStartsFromLeftmost && WildPaytable.TryGetValue(wildCount, out double wildPayout))
                {
                    wildOnlyPayout = wildPayout;
                }

                // Calculate symbol+wild payout
                // Allow wild substitution even with 1 symbol if total count meets minimum requirement
                if (hasSymbols && symbolType != null && symbolConfigs.ContainsKey(symbolType))
                {
                    int totalCount = symbolCount + wildCount;
                    // Check if total count meets minimum requirement for this symbol (usually 3)
                    if (totalCount >= 3 && symbolConfigs[symbolType].Payouts.TryGetValue(totalCount, out double symbolPayout))
                    {
                        symbolWithWildPayout = symbolPayout;
                    }
                }

                // Take the higher payout
                if (wildOnlyPayout > symbolWithWildPayout)
                {
                    wildWin += wildOnlyPayout;
                    winningPositions.AddRange(wildPositions);
                    
                    // Create full payline path (all 5 positions)
                    var fullPaylinePath = new List<Position>();
                    for (int col = 0; col < 5; col++)
                    {
                        fullPaylinePath.Add(new Position { Col = col, Row = line[col] });
                    }
                    
                    // Create winning line
                    winningLines.Add(new WinningLine
                    {
                        Positions = winningPositions,
                        Symbol = "WILD", // Use generic wild symbol name
                        Count = wildCount,
                        WinAmount = wildOnlyPayout,
                        PaylineType = "wild",
                        PaylineIndex = paylineIndex,
                        FullPaylinePath = fullPaylinePath
                    });
                }
                else if (symbolWithWildPayout > 0)
                {
                    wildWin += symbolWithWildPayout;
                    winningPositions.AddRange(symbolPositions);
                    winningPositions.AddRange(wildPositions);
                    
                    // Create full payline path (all 5 positions)
                    var fullPaylinePath = new List<Position>();
                    for (int col = 0; col < 5; col++)
                    {
                        fullPaylinePath.Add(new Position { Col = col, Row = line[col] });
                    }
                    
                    // Create winning line
                    winningLines.Add(new WinningLine
                    {
                        Positions = winningPositions,
                        Symbol = symbolType,
                        Count = symbolCount + wildCount,
                        WinAmount = symbolWithWildPayout,
                        PaylineType = "wild",
                        PaylineIndex = paylineIndex,
                        FullPaylinePath = fullPaylinePath
                    });
                }
            }

            return wildWin;
        }

        // Optimized version for simulations without winningLines tracking
        public static double EvaluateWildLineWinsOptimized(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            double wildWin = 0;

            foreach (var line in paylines)
            {
                int wildCount = 0;
                int symbolCount = 0;
                string symbolType = null;
                bool hasSymbols = false;
                
                // OFFICIAL BLOODSUCKERS RULE: Wild-only wins must start from leftmost reel (column 0)
                bool wildOnlyStartsFromLeftmost = false;
                int firstWildColumn = -1;

                // Count wilds and symbols separately - SAME LOGIC AS MASTER METHOD
                for (int col = 0; col < 5; col++)
                {
                    int row = line[col];
                    string symbol = grid[col][row];

                    if (symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsWild)
                    {
                        if (firstWildColumn == -1)
                        {
                            firstWildColumn = col; // Track the first wild position
                        }
                        wildCount++;
                    }
                    else if (symbolConfigs.ContainsKey(symbol) && !symbolConfigs[symbol].IsWild && !symbolConfigs[symbol].IsScatter && !symbolConfigs[symbol].IsBonus)
                    {
                        // Regular symbol
                        if (symbolType == null)
                        {
                            symbolType = symbol;
                            symbolCount = 1;
                            hasSymbols = true;
                        }
                        else if (symbol == symbolType)
                        {
                            symbolCount++;
                        }
                        else
                        {
                            // Different symbol, break the line
                            break;
                        }
                    }
                    else
                    {
                        // Invalid symbol, break the line
                        break;
                    }
                }
                
                // Check if wild-only win starts from leftmost reel
                wildOnlyStartsFromLeftmost = (firstWildColumn == 0);

                // Only process this payline if it actually contains wilds
                if (wildCount == 0)
                    continue; // Skip this payline - no wilds to process

                // Compare wild-only vs symbol+wild wins, take the highest
                double wildOnlyPayout = 0;
                double symbolWithWildPayout = 0;

                // OFFICIAL BLOODSUCKERS RULE: Wild-of-a-kind pays from 2 in a row using specific wild paytable
                // BUT ONLY if wilds start from the leftmost reel (column 0)
                var wildSymbol = symbolConfigs.FirstOrDefault(kvp => kvp.Value.IsWild).Key;
                if (wildCount >= 2 && wildOnlyStartsFromLeftmost && wildSymbol != null && symbolConfigs[wildSymbol].Payouts.TryGetValue(wildCount, out double wildPayout))
                {
                    wildOnlyPayout = wildPayout;
                }

                // Calculate symbol+wild payout - SAME LOGIC AS MASTER METHOD
                // Allow wild substitution even with 1 symbol if total count meets minimum requirement
                if (hasSymbols && symbolType != null && symbolConfigs.ContainsKey(symbolType))
                {
                    int totalCount = symbolCount + wildCount;
                    // Check if total count meets minimum requirement for this symbol (usually 3)
                    if (totalCount >= 3 && symbolConfigs[symbolType].Payouts.TryGetValue(totalCount, out double symbolPayout))
                    {
                        symbolWithWildPayout = symbolPayout;
                    }
                }

                // Take the higher payout
                double payout = Math.Max(wildOnlyPayout, symbolWithWildPayout);
                wildWin += payout;
            }

            return wildWin;
        }

        public static double EvaluateScattersWithLines(string[][] grid, Dictionary<string, SymbolConfig> symbolConfigs, bool isFreeSpin, out List<WinningLine> winningLines, out int scatterCount, int betAmount)
        {
            winningLines = new List<WinningLine>();
            scatterCount = grid.SelectMany(col => col)
                .Count(symbol => symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsScatter);

            if (scatterCount >= 2) // Changed from >= 3 to >= 2 to match original logic
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

                // Use original hardcoded scatter payout logic from SlotEngine
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

        // Optimized version for simulations without winningLines tracking
        public static double EvaluateScattersOptimized(string[][] grid, int betAmount, out int scatterCount, Dictionary<string, SymbolConfig> symbolConfigs)
        {
            scatterCount = grid.SelectMany(col => col)
                               .Count(sym => symbolConfigs.ContainsKey(sym) && symbolConfigs[sym].IsScatter);

            if (scatterCount >= 2) // SAME LOGIC AS MASTER METHOD - minimum 2 scatters required for wins
            {
                double multiplier = 0;
                int freeSpinsAwarded = 0;

                // Use original hardcoded scatter payout logic - SAME AS MASTER METHOD
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

            return 0; // No scatter win if less than 2 scatters - SAME AS MASTER METHOD
        }

        public static string CreateSvgPath(List<Position> positions)
        {
            if (positions.Count == 0) 
                return "";
            
            var path = new StringBuilder();
            bool first = true;
            
            foreach (var pos in positions)
            {
                // Convert grid position to SVG coordinates
                int x = pos.Col * 100 + 50; // 100px per column, center at 50
                int y = pos.Row * 60 + 30;  // 60px per row, center at 30
                
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
            
            return path.ToString();
        }

        public static bool CheckBonusTrigger(string[][] grid, List<int[]> paylines, Dictionary<string, SymbolConfig> symbolConfigs, int scatterCount, ref string bonusLog)
        {
            foreach (var line in paylines)
            {
                int count = 0;
                var bonusPositions = new List<Position>();
                
                for (int col = 0; col < 5; col++)
                {
                    string symbol = grid[col][line[col]];
                    // Check if symbol is bonus symbol using configuration
                    if (symbolConfigs.ContainsKey(symbol) && symbolConfigs[symbol].IsBonus)
                    {
                        count++;
                        bonusPositions.Add(new Position { Col = col, Row = line[col] });
                    }
                    else break;
                }

                if (count >= 3) // Need 3 or more bonus symbols to trigger
                {
                    bonusLog = $"🎰 BONUS TRIGGERED! Coffin symbols x{count} on payline [{string.Join(",", line)}] - Coffin Selection Bonus Game!";
                    return true;
                }
            }
            return false;
        }

        public static double SimulateBonusGame(GameConfig config, double currentRtpBeforeSpin)
        {
            // BloodSuckers Bonus Game: Coffin Selection
            // The actual game has a coffin selection bonus where players pick coffins to reveal prizes
            // Each coffin contains different multipliers or cash prizes
            
            // Simulate coffin selection bonus game
            int coffinSelections = 3; // Player gets 3 coffin picks
            double totalBonusWin = 0;
            
            for (int pick = 1; pick <= coffinSelections; pick++)
            {
                // Different coffin types with different prize distributions
                double coffinWin = 0;
                
                // Randomly determine coffin type and prize
                double randomValue = _rng.NextDouble();
                
                if (randomValue < 0.4) // 40% chance - Small prize coffin
                {
                    coffinWin = 5 + _rng.NextDouble() * 10; // 5-15 coins
                }
                else if (randomValue < 0.7) // 30% chance - Medium prize coffin
                {
                    coffinWin = 15 + _rng.NextDouble() * 20; // 15-35 coins
                }
                else if (randomValue < 0.9) // 20% chance - Large prize coffin
                {
                    coffinWin = 35 + _rng.NextDouble() * 30; // 35-65 coins
                }
                else // 10% chance - Jackpot coffin
                {
                    coffinWin = 65 + _rng.NextDouble() * 50; // 65-115 coins
                }
                
                // Apply RTP scaling to balance the game
                double rtpDeficit = Math.Max(0, config.RtpTarget - currentRtpBeforeSpin);
                double rtpMultiplier = 1.0 + (rtpDeficit * 0.5); // Scale up when RTP is low
                coffinWin *= rtpMultiplier;
                
                totalBonusWin += coffinWin;
            }
            
            // Cap the maximum bonus win to prevent excessive payouts
            double maxBonusWin = 150; // Cap at 150 coins
            totalBonusWin = Math.Min(totalBonusWin, maxBonusWin);
            
            return totalBonusWin;
        }
    }
}
