using Microsoft.AspNetCore.Mvc;
using BloodSuckersSlot.Api.Services;
using BloodSuckersSlot.Shared.Models;
using System.Security.Claims;

namespace BloodSuckersSlot.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IGamingEntityAuthService _entityAuthService;
        private readonly IJwtService _jwtService;
        private readonly IPlayerSessionService _playerSessionService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IGamingEntityAuthService entityAuthService, IJwtService jwtService, IPlayerSessionService playerSessionService, ILogger<AuthController> logger)
        {
            _entityAuthService = entityAuthService;
            _jwtService = jwtService;
            _playerSessionService = playerSessionService;
            _logger = logger;
        }

        /// <summary>
        /// Authenticate entity and return JWT token
        /// </summary>
        [HttpPost("login")]
        public async Task<ActionResult<EntityLoginResponse>> Login([FromBody] EntityLoginRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
                {
                    return BadRequest(new EntityLoginResponse
                    {
                        Success = false,
                        ErrorMessage = "Username and password are required"
                    });
                }

                var entity = await _entityAuthService.AuthenticateEntityAsync(request.Username, request.Password);
                if (entity == null)
                {
                    return Unauthorized(new EntityLoginResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid username or password"
                    });
                }

                var token = _jwtService.GenerateToken(entity);
                var refreshToken = _jwtService.GenerateRefreshToken();

                var entityInfo = new EntityInfo
                {
                    Id = entity.Id,
                    Username = entity.Username,
                    Email = entity.Email,
                    Role = entity.Role,
                    Active = entity.Active,
                    LastLoginAt = entity.LastLoginAt,
                    SuperAgentId = entity.SuperAgentId,
                    AgentId = entity.AgentId,
                    TokenId = entity.TokenId,
                    GroupId = entity.GroupId,
                    FirstName = entity.FirstName,
                    LastName = entity.LastName,
                    PhoneNumber = entity.PhoneNumber,
                    DateOfBirth = entity.DateOfBirth,
                    Balance = entity.Balance,
                    PlayerStatus = entity.PlayerStatus
                };

                return Ok(new EntityLoginResponse
                {
                    Success = true,
                    Token = token,
                    RefreshToken = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(60), // Should match JWT settings
                    Entity = entityInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for entity: {Username}", request.Username);
                return StatusCode(500, new EntityLoginResponse
                {
                    Success = false,
                    ErrorMessage = "Internal server error"
                });
            }
        }

        /// <summary>
        /// Refresh JWT token using refresh token
        /// </summary>
        [HttpPost("refresh")]
        public async Task<ActionResult<UserLoginResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            try
            {
                // In a real implementation, you would validate the refresh token against a database
                // For now, we'll just generate a new token if the refresh token is provided
                if (string.IsNullOrEmpty(request.RefreshToken))
                {
                    return BadRequest(new UserLoginResponse
                    {
                        Success = false,
                        ErrorMessage = "Refresh token is required"
                    });
                }

                // TODO: Implement refresh token validation against database
                // For now, return error
                return Unauthorized(new UserLoginResponse
                {
                    Success = false,
                    ErrorMessage = "Refresh token validation not implemented"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return StatusCode(500, new UserLoginResponse
                {
                    Success = false,
                    ErrorMessage = "Internal server error"
                });
            }
        }

        /// <summary>
        /// Get current entity information from JWT token
        /// </summary>
        [HttpGet("me")]
        public async Task<ActionResult<EntityInfo>> GetCurrentEntity()
        {
            try
            {
                var entityId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(entityId))
                {
                    return Unauthorized();
                }

                var entity = await _entityAuthService.GetEntityByIdAsync(entityId);
                if (entity == null)
                {
                    return NotFound();
                }

                var entityInfo = new EntityInfo
                {
                    Id = entity.Id,
                    Username = entity.Username,
                    Email = entity.Email,
                    Role = entity.Role,
                    Active = entity.Active,
                    LastLoginAt = entity.LastLoginAt,
                    SuperAgentId = entity.SuperAgentId,
                    AgentId = entity.AgentId,
                    TokenId = entity.TokenId,
                    GroupId = entity.GroupId,
                    FirstName = entity.FirstName,
                    LastName = entity.LastName,
                    PhoneNumber = entity.PhoneNumber,
                    DateOfBirth = entity.DateOfBirth,
                    Balance = entity.Balance,
                    PlayerStatus = entity.PlayerStatus
                };

                return Ok(entityInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current entity");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Create default admin user if it doesn't exist
        /// </summary>
        [HttpPost("create-admin")]
        public async Task<ActionResult> CreateAdmin()
        {
            try
            {
                var admin = await _entityAuthService.CreateDefaultAdminAsync();
                if (admin != null)
                {
                    return Ok(new { 
                        Success = true,
                        Message = "Admin user created successfully",
                        Username = admin.Username,
                        Role = admin.Role.ToString(),
                        RoleValue = (int)admin.Role,
                        Password = "admin123"
                    });
                }
                else
                {
                    return Ok(new { 
                        Success = true,
                        Message = "Admin user already exists"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating admin user");
                return StatusCode(500, new { 
                    Success = false,
                    Error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Test endpoint to check if admin user exists
        /// </summary>
        [HttpGet("test-admin")]
        public async Task<ActionResult> TestAdminExists()
        {
            try
            {
                var admin = await _entityAuthService.GetEntityByUsernameAsync("admin");
                if (admin != null)
                {
                    return Ok(new { 
                        Exists = true, 
                        Username = admin.Username, 
                        Role = admin.Role.ToString(),
                        RoleValue = (int)admin.Role,
                        Active = admin.Active,
                        HasPassword = !string.IsNullOrEmpty(admin.PasswordHash)
                    });
                }
                else
                {
                    return Ok(new { Exists = false });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Delete admin user (for testing purposes)
        /// </summary>
        [HttpPost("delete-admin")]
        public async Task<ActionResult> DeleteAdmin()
        {
            try
            {
                var admin = await _entityAuthService.GetEntityByUsernameAsync("admin");
                if (admin != null)
                {
                    await _entityAuthService.DeleteEntityAsync(admin.Id);
                    return Ok(new { 
                        Success = true,
                        Message = "Admin user deleted successfully"
                    });
                }
                else
                {
                    return Ok(new { 
                        Success = true,
                        Message = "Admin user does not exist"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting admin user");
                return StatusCode(500, new { 
                    Success = false,
                    Error = ex.Message 
                });
            }
        }
        /// <summary>
        /// Get default password for a player (for testing purposes)
        /// </summary>
        [HttpGet("default-password/{username}")]
        public ActionResult GetDefaultPassword(string username)
        {
            try
            {
                // Generate default password based on username
                var defaultPassword = $"player{username}123";
                return Ok(new { 
                    Username = username,
                    DefaultPassword = defaultPassword,
                    Message = "This is the default password for the player"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting default password for username: {Username}", username);
                return StatusCode(500, new { 
                    Success = false,
                    Error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Logout user (client-side token removal)
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // End the player session when logging out
                var playerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(playerId))
                {
                    await _playerSessionService.EndSessionByPlayerIdAsync(playerId);
                    _logger.LogInformation("Ended session for player {PlayerId} on logout", playerId);
                }

                // In a stateless JWT implementation, logout is handled client-side
                // by removing the token from storage
                return Ok(new { message = "Logged out successfully and session ended" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return Ok(new { message = "Logged out successfully" }); // Still return success for client
            }
        }
    }
}
