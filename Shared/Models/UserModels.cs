using System.ComponentModel.DataAnnotations;

namespace BloodSuckersSlot.Shared.Models
{
    public enum UserRole
    {
        ADMIN,
        PLAYER
    }

    public class User
    {
        public string Id { get; set; } = string.Empty;
        
        [Required]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        public string PasswordHash { get; set; } = string.Empty;
        
        public UserRole Role { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastLoginAt { get; set; }
        
        // For Player role - can be assigned to multiple groups
        public List<string> GroupIds { get; set; } = new List<string>();
        
        // Additional fields for Player
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public decimal? Balance { get; set; }
        public string? PlayerStatus { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class UserLoginRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class UserLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public UserInfo? User { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class UserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public List<string> GroupIds { get; set; } = new List<string>();
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
    }

    public class JwtSettings
    {
        public string SecretKey { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int ExpirationMinutes { get; set; } = 60;
        public int RefreshTokenExpirationDays { get; set; } = 7;
    }

    public class RefreshTokenRequest
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    // Entity-based authentication models
    public class EntityLoginRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class EntityLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public EntityInfo? Entity { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class EntityInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public EntityRole Role { get; set; }
        public bool Active { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? SuperAgentId { get; set; }
        public string? AgentId { get; set; }
        public string? TokenId { get; set; }
        public string? GroupId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public decimal? Balance { get; set; }
        public string? PlayerStatus { get; set; }
    }
}
