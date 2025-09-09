using MongoDB.Driver;
using BloodSuckersSlot.Shared.Models;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;

namespace BloodSuckersSlot.Api.Services
{
    public interface IUserService
    {
        Task<User?> GetUserByUsernameAsync(string username);
        Task<User?> GetUserByIdAsync(string id);
        Task<User?> CreateUserAsync(User user);
        Task<User?> UpdateUserAsync(string id, User user);
        Task<bool> DeleteUserAsync(string id);
        Task<bool> ValidatePasswordAsync(string password, string passwordHash);
        string HashPassword(string password);
        Task<User?> AuthenticateUserAsync(string username, string password);
        Task UpdateLastLoginAsync(string userId);
        Task<List<User>> GetUsersByRoleAsync(UserRole role);
        Task<List<User>> GetPlayersByGroupIdAsync(string groupId);
    }

    public class UserService : IUserService
    {
        private readonly IMongoCollection<User> _collection;
        private readonly ILogger<UserService> _logger;

        public UserService(IMongoDatabase database, ILogger<UserService> logger)
        {
            _collection = database.GetCollection<User>("users");
            _logger = logger;
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            try
            {
                return await _collection.Find(x => x.Username == username).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by username: {Username}", username);
                throw;
            }
        }

        public async Task<User?> GetUserByIdAsync(string id)
        {
            try
            {
                return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by id: {Id}", id);
                throw;
            }
        }

        public async Task<User?> CreateUserAsync(User user)
        {
            try
            {
                user.Id = ObjectId.GenerateNewId().ToString();
                user.CreatedAt = DateTime.UtcNow;
                user.PasswordHash = HashPassword(user.PasswordHash); // Assuming PasswordHash contains plain password
                
                await _collection.InsertOneAsync(user);
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user: {Username}", user.Username);
                throw;
            }
        }

        public async Task<User?> UpdateUserAsync(string id, User user)
        {
            try
            {
                user.UpdatedAt = DateTime.UtcNow;
                var result = await _collection.ReplaceOneAsync(x => x.Id == id, user);
                return result.IsAcknowledged ? user : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user: {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(string id)
        {
            try
            {
                var result = await _collection.DeleteOneAsync(x => x.Id == id);
                return result.IsAcknowledged && result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user: {Id}", id);
                throw;
            }
        }

        public async Task<bool> ValidatePasswordAsync(string password, string passwordHash)
        {
            try
            {
                var hashedPassword = HashPassword(password);
                return hashedPassword == passwordHash;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating password");
                return false;
            }
        }

        public string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        public async Task<User?> AuthenticateUserAsync(string username, string password)
        {
            try
            {
                var user = await GetUserByUsernameAsync(username);
                if (user == null || !user.IsActive)
                {
                    return null;
                }

                if (await ValidatePasswordAsync(password, user.PasswordHash))
                {
                    await UpdateLastLoginAsync(user.Id);
                    return user;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating user: {Username}", username);
                throw;
            }
        }

        public async Task UpdateLastLoginAsync(string userId)
        {
            try
            {
                var update = Builders<User>.Update.Set(x => x.LastLoginAt, DateTime.UtcNow);
                await _collection.UpdateOneAsync(x => x.Id == userId, update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last login for user: {UserId}", userId);
                throw;
            }
        }

        public async Task<List<User>> GetUsersByRoleAsync(UserRole role)
        {
            try
            {
                return await _collection.Find(x => x.Role == role && x.IsActive).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users by role: {Role}", role);
                throw;
            }
        }

        public async Task<List<User>> GetPlayersByGroupIdAsync(string groupId)
        {
            try
            {
                return await _collection.Find(x => x.Role == UserRole.PLAYER && x.GroupIds.Contains(groupId) && x.IsActive).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting players by group id: {GroupId}", groupId);
                throw;
            }
        }
    }
}
