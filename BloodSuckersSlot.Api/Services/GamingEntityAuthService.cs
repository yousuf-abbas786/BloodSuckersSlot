using MongoDB.Driver;
using BloodSuckersSlot.Shared.Models;
using BloodSuckersSlot.Api.Models;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using ApiGamingEntity = BloodSuckersSlot.Api.Models.GamingEntity;

namespace BloodSuckersSlot.Api.Services
{
    public interface IGamingEntityAuthService
    {
        Task<BloodSuckersSlot.Shared.Models.GamingEntity?> GetEntityByUsernameAsync(string username);
        Task<BloodSuckersSlot.Shared.Models.GamingEntity?> GetEntityByIdAsync(string id);
        Task<bool> ValidatePasswordAsync(string password, string passwordHash);
        string HashPassword(string password);
        Task<BloodSuckersSlot.Shared.Models.GamingEntity?> AuthenticateEntityAsync(string username, string password);
        Task UpdateLastLoginAsync(string entityId);
        Task<List<BloodSuckersSlot.Shared.Models.GamingEntity>> GetEntitiesByRoleAsync(EntityRole role);
        Task<List<BloodSuckersSlot.Shared.Models.GamingEntity>> GetPlayersByGroupIdAsync(string groupId);
        Task<BloodSuckersSlot.Shared.Models.GamingEntity?> CreateDefaultAdminAsync();
        Task DeleteEntityAsync(string entityId);
    }

    public class GamingEntityAuthService : IGamingEntityAuthService
    {
        private readonly IMongoCollection<ApiGamingEntity> _collection;
        private readonly ILogger<GamingEntityAuthService> _logger;

        public GamingEntityAuthService(IMongoDatabase database, ILogger<GamingEntityAuthService> logger)
        {
            _collection = database.GetCollection<ApiGamingEntity>("gamingEntities");
            _logger = logger;
        }

        public async Task<BloodSuckersSlot.Shared.Models.GamingEntity?> GetEntityByUsernameAsync(string username)
        {
            try
            {
                var entity = await _collection.Find(x => x.Username == username).FirstOrDefaultAsync();
                return entity?.ToShared();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by username: {Username}", username);
                throw;
            }
        }

        public async Task<BloodSuckersSlot.Shared.Models.GamingEntity?> GetEntityByIdAsync(string id)
        {
            try
            {
                var entity = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
                return entity?.ToShared();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by id: {Id}", id);
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

        public async Task<BloodSuckersSlot.Shared.Models.GamingEntity?> AuthenticateEntityAsync(string username, string password)
        {
            try
            {
                var entity = await GetEntityByUsernameAsync(username);
                if (entity == null || !entity.Active || string.IsNullOrEmpty(entity.PasswordHash))
                {
                    return null;
                }

                if (await ValidatePasswordAsync(password, entity.PasswordHash))
                {
                    await UpdateLastLoginAsync(entity.Id);
                    return entity;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating entity: {Username}", username);
                throw;
            }
        }

        public async Task UpdateLastLoginAsync(string entityId)
        {
            try
            {
                var update = Builders<ApiGamingEntity>.Update.Set(x => x.LastLoginDate, DateTime.UtcNow);
                await _collection.UpdateOneAsync(x => x.Id == entityId, update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last login for entity: {EntityId}", entityId);
                throw;
            }
        }

        public async Task<List<BloodSuckersSlot.Shared.Models.GamingEntity>> GetEntitiesByRoleAsync(EntityRole role)
        {
            try
            {
                var entities = await _collection.Find(x => x.Role == role && x.Active).ToListAsync();
                return entities.Select(e => e.ToShared()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities by role: {Role}", role);
                throw;
            }
        }

        public async Task<List<BloodSuckersSlot.Shared.Models.GamingEntity>> GetPlayersByGroupIdAsync(string groupId)
        {
            try
            {
                var entities = await _collection.Find(x => x.Role == EntityRole.PLAYER && x.Active).ToListAsync();
                return entities.Select(e => e.ToShared()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting players by group id: {GroupId}", groupId);
                throw;
            }
        }

        public async Task<BloodSuckersSlot.Shared.Models.GamingEntity?> CreateDefaultAdminAsync()
        {
            try
            {
                // Check if default admin already exists
                var existingAdmin = await _collection.Find(x => x.Username == "admin").FirstOrDefaultAsync();
                if (existingAdmin != null)
                {
                    _logger.LogInformation("Default admin already exists with role: {Role} ({RoleValue})", existingAdmin.Role.ToString(), (int)existingAdmin.Role);
                    
                    // If the existing admin has the wrong role, delete and recreate
                    if (existingAdmin.Role != EntityRole.ADMIN)
                    {
                        _logger.LogInformation("Existing admin has wrong role {OldRole}, deleting and recreating", existingAdmin.Role.ToString());
                        await _collection.DeleteOneAsync(x => x.Id == existingAdmin.Id);
                    }
                    else
                    {
                        return existingAdmin.ToShared();
                    }
                }

                // Create default admin entity
                var adminEntity = new ApiGamingEntity
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Username = "admin",
                    Email = "admin@bloodsuckersslot.com",
                    PasswordHash = HashPassword("admin123"),
                    Role = EntityRole.ADMIN,
                    Active = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _logger.LogInformation("Creating admin entity with role: {Role} ({RoleValue})", adminEntity.Role.ToString(), (int)adminEntity.Role);

                await _collection.InsertOneAsync(adminEntity);
                _logger.LogInformation("Default admin entity created successfully");
                return adminEntity.ToShared();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating default admin entity");
                throw;
            }
        }

        public async Task DeleteEntityAsync(string entityId)
        {
            try
            {
                var result = await _collection.DeleteOneAsync(x => x.Id == entityId);
                if (result.DeletedCount == 0)
                {
                    _logger.LogWarning("No entity found with ID: {EntityId}", entityId);
                }
                else
                {
                    _logger.LogInformation("Entity deleted successfully: {EntityId}", entityId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting entity: {EntityId}", entityId);
                throw;
            }
        }
    }
}
