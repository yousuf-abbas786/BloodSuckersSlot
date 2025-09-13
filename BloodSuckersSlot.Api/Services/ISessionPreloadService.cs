using Shared.Models;

namespace BloodSuckersSlot.Api.Services
{
    /// <summary>
    /// Service for preloading player sessions to improve spin performance
    /// </summary>
    public interface ISessionPreloadService
    {
        /// <summary>
        /// Preload player session into cache for faster access
        /// </summary>
        /// <param name="playerId">Player ID to preload session for</param>
        /// <returns>True if session was successfully preloaded</returns>
        Task<bool> PreloadSessionAsync(string playerId);

        /// <summary>
        /// Get cached session if available
        /// </summary>
        /// <param name="playerId">Player ID</param>
        /// <returns>Cached session or null if not cached</returns>
        Task<PlayerSessionResponse?> GetCachedSessionAsync(string playerId);

        /// <summary>
        /// Update cached session
        /// </summary>
        /// <param name="playerId">Player ID</param>
        /// <param name="session">Updated session</param>
        void UpdateCachedSession(string playerId, PlayerSessionResponse session);

        /// <summary>
        /// Remove session from cache
        /// </summary>
        /// <param name="playerId">Player ID</param>
        void RemoveCachedSession(string playerId);
    }
}
