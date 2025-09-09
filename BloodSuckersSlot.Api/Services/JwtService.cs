using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BloodSuckersSlot.Shared.Models;

namespace BloodSuckersSlot.Api.Services
{
    public interface IJwtService
    {
        string GenerateToken(GamingEntity entity);
        string GenerateRefreshToken();
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
        bool ValidateToken(string token);
    }

    public class JwtService : IJwtService
    {
        private readonly JwtSettings _jwtSettings;
        private readonly ILogger<JwtService> _logger;

        public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
        {
            _logger = logger;
            _jwtSettings = new JwtSettings
            {
                SecretKey = configuration["Jwt:SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!",
                Issuer = configuration["Jwt:Issuer"] ?? "BloodSuckersSlot",
                Audience = configuration["Jwt:Audience"] ?? "BloodSuckersSlotUsers",
                ExpirationMinutes = int.Parse(configuration["Jwt:ExpirationMinutes"] ?? "60"),
                RefreshTokenExpirationDays = int.Parse(configuration["Jwt:RefreshTokenExpirationDays"] ?? "7")
            };
        }

        public string GenerateToken(GamingEntity entity)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, entity.Id),
                new(ClaimTypes.Name, entity.Username),
                new(ClaimTypes.Email, entity.Email),
                new(ClaimTypes.Role, entity.Role.ToString()),
                new("IsActive", entity.Active.ToString()),
                new("LastLoginAt", entity.LastLoginAt?.ToString("O") ?? ""),
                new("SuperAgentId", entity.SuperAgentId ?? ""),
                new("AgentId", entity.AgentId ?? ""),
                new("TokenId", entity.TokenId ?? ""),
                new("GroupId", entity.GroupId ?? "")
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtSettings.SecretKey)),
                ValidateLifetime = false, // We don't care about the token's expiration date
                ValidIssuer = _jwtSettings.Issuer,
                ValidAudience = _jwtSettings.Audience
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);

            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException("Invalid token");
            }

            return principal;
        }

        public bool ValidateToken(string token)
        {
            try
            {
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidateIssuer = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtSettings.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidIssuer = _jwtSettings.Issuer,
                    ValidAudience = _jwtSettings.Audience
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Token validation failed: {Error}", ex.Message);
                return false;
            }
        }
    }
}
