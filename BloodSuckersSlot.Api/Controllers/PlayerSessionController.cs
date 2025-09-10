using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BloodSuckersSlot.Api.Services;
using Shared.Models;
using System.Security.Claims;

namespace BloodSuckersSlot.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlayerSessionController : ControllerBase
    {
        private readonly IPlayerSessionService _playerSessionService;
        private readonly ILogger<PlayerSessionController> _logger;

        public PlayerSessionController(IPlayerSessionService playerSessionService, ILogger<PlayerSessionController> logger)
        {
            _playerSessionService = playerSessionService;
            _logger = logger;
        }

        /// <summary>
        /// Start a new player session
        /// </summary>
        [HttpPost("start")]
        public async Task<ActionResult<PlayerSessionResponse>> StartSession([FromBody] StartSessionRequest request)
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = User.FindFirst(ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(username))
                {
                    return Unauthorized("Invalid user context");
                }

                // Override with authenticated user data
                request.PlayerId = playerId;
                request.Username = username;

                var session = await _playerSessionService.StartSessionAsync(request);
                if (session == null)
                {
                    return BadRequest("Failed to start session");
                }

                _logger.LogInformation("Started session {SessionId} for player {PlayerId}", session.SessionId, playerId);
                return Ok(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting session for player");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get current active session for the authenticated player
        /// </summary>
        [HttpGet("current")]
        public async Task<ActionResult<PlayerSessionResponse>> GetCurrentSession()
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                var session = await _playerSessionService.GetActiveSessionAsync(playerId);
                if (session == null)
                {
                    return NotFound("No active session found");
                }

                return Ok(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current session");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get current active session for the authenticated player (alias for /current)
        /// </summary>
        [HttpGet("active")]
        public async Task<ActionResult<PlayerSessionResponse>> GetActiveSession()
        {
            return await GetCurrentSession();
        }

        /// <summary>
        /// Get session by ID
        /// </summary>
        [HttpGet("{sessionId}")]
        public async Task<ActionResult<PlayerSessionResponse>> GetSession(string sessionId)
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                var session = await _playerSessionService.GetSessionAsync(sessionId);
                if (session == null)
                {
                    return NotFound("Session not found");
                }

                // Ensure player can only access their own sessions
                if (session.PlayerId != playerId)
                {
                    return Forbid("Access denied");
                }

                return Ok(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// End current active session
        /// </summary>
        [HttpPost("end")]
        public async Task<ActionResult> EndCurrentSession()
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                var session = await _playerSessionService.GetActiveSessionAsync(playerId);
                if (session == null)
                {
                    return NotFound("No active session found");
                }

                var success = await _playerSessionService.EndSessionAsync(session.SessionId);
                if (!success)
                {
                    return BadRequest("Failed to end session");
                }

                _logger.LogInformation("Ended session {SessionId} for player {PlayerId}", session.SessionId, playerId);
                return Ok(new { message = "Session ended successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending current session");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Update session statistics after a spin
        /// </summary>
        [HttpPost("update-stats")]
        public async Task<ActionResult> UpdateSessionStats([FromBody] UpdateSessionStatsRequest request)
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                // Override with authenticated user data
                request.PlayerId = playerId;

                var success = await _playerSessionService.UpdateSessionStatsAsync(request);
                if (!success)
                {
                    return BadRequest("Failed to update session stats");
                }

                return Ok(new { message = "Session stats updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating session stats");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get player lifetime statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult<PlayerStatsResponse>> GetPlayerStats()
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                var stats = await _playerSessionService.GetPlayerStatsAsync(playerId);
                if (stats == null)
                {
                    return NotFound("Player stats not found");
                }

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting player stats");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get player session history
        /// </summary>
        [HttpGet("history")]
        public async Task<ActionResult<List<PlayerSessionResponse>>> GetPlayerSessions([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                var sessions = await _playerSessionService.GetPlayerSessionsAsync(playerId, pageNumber, pageSize);
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting player sessions");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Update last activity timestamp
        /// </summary>
        [HttpPost("activity")]
        public async Task<ActionResult> UpdateActivity()
        {
            try
            {
                var playerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(playerId))
                {
                    return Unauthorized("Invalid user context");
                }

                var session = await _playerSessionService.GetActiveSessionAsync(playerId);
                if (session == null)
                {
                    return NotFound("No active session found");
                }

                var success = await _playerSessionService.UpdateLastActivityAsync(session.SessionId);
                if (!success)
                {
                    return BadRequest("Failed to update activity");
                }

                return Ok(new { message = "Activity updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating activity");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all active sessions (Admin only)
        /// </summary>
        [HttpGet("admin/active")]
        [Authorize(Roles = "ADMIN")]
        public async Task<ActionResult<List<PlayerSessionResponse>>> GetActiveSessions()
        {
            try
            {
                var sessions = await _playerSessionService.GetActiveSessionsAsync();
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active sessions");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Cleanup inactive sessions (Admin only)
        /// </summary>
        [HttpPost("admin/cleanup")]
        [Authorize(Roles = "ADMIN")]
        public async Task<ActionResult> CleanupInactiveSessions([FromQuery] int hoursThreshold = 24)
        {
            try
            {
                var threshold = TimeSpan.FromHours(hoursThreshold);
                await _playerSessionService.CleanupInactiveSessionsAsync(threshold);
                
                return Ok(new { message = $"Cleaned up sessions inactive for more than {hoursThreshold} hours" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up inactive sessions");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// End session by player ID (for logout)
        /// </summary>
        [HttpPost("end-session/{playerId}")]
        public async Task<IActionResult> EndSession(string playerId)
        {
            try
            {
                var success = await _playerSessionService.EndSessionByPlayerIdAsync(playerId);
                if (!success)
                {
                    return NotFound($"No active session found for player {playerId}");
                }

                return Ok(new { message = "Session ended successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session for player {PlayerId}", playerId);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Reset player stats (for testing)
        /// </summary>
        [HttpPost("reset-stats/{playerId}")]
        public async Task<IActionResult> ResetPlayerStats(string playerId)
        {
            try
            {
                await _playerSessionService.ResetPlayerStatsAsync(playerId);
                return Ok(new { message = "Player stats reset successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting player stats for {PlayerId}", playerId);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
