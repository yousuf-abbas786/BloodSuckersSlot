using System;

namespace Shared
{
    public static class BettingSystem
    {
        /// <summary>
        /// Calculates the total bet in coins based on level
        /// </summary>
        /// <param name="baseBetPerLevel">Base bet per level (default 25)</param>
        /// <param name="level">Current bet level (1-4)</param>
        /// <returns>Total bet in coins</returns>
        public static int CalculateBetInCoins(int baseBetPerLevel, int level)
        {
            return baseBetPerLevel * level;
        }

        /// <summary>
        /// Calculates the total monetary bet based on level and coin value
        /// </summary>
        /// <param name="baseBetPerLevel">Base bet per level (default 25)</param>
        /// <param name="level">Current bet level (1-4)</param>
        /// <param name="coinValue">Monetary value per coin ($0.01-$0.50)</param>
        /// <returns>Total monetary bet</returns>
        public static decimal CalculateTotalBet(int baseBetPerLevel, int level, decimal coinValue)
        {
            int betInCoins = CalculateBetInCoins(baseBetPerLevel, level);
            return betInCoins * coinValue;
        }

        /// <summary>
        /// Calculates the monetary payout for a given coin payout
        /// </summary>
        /// <param name="coinPayout">Payout in coins</param>
        /// <param name="coinValue">Monetary value per coin</param>
        /// <returns>Monetary payout</returns>
        public static decimal CalculatePayout(int coinPayout, decimal coinValue)
        {
            return coinPayout * coinValue;
        }

        /// <summary>
        /// Validates bet parameters
        /// </summary>
        /// <param name="level">Bet level</param>
        /// <param name="coinValue">Coin value</param>
        /// <param name="maxLevel">Maximum allowed level</param>
        /// <param name="minCoinValue">Minimum coin value</param>
        /// <param name="maxCoinValue">Maximum coin value</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateBet(int level, decimal coinValue, int maxLevel, decimal minCoinValue, decimal maxCoinValue)
        {
            return level >= 1 && level <= maxLevel && 
                   coinValue >= minCoinValue && coinValue <= maxCoinValue;
        }
    }
} 