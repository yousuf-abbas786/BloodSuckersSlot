using MongoDB.Driver;
using BloodSuckersSlot.Api.Models;
using BloodSuckersSlot.Shared.Models;
using System.Linq.Expressions;
using MongoDB.Bson;
using ApiGamingEntity = BloodSuckersSlot.Api.Models.GamingEntity;

namespace BloodSuckersSlot.Api.Services
{
    public interface IGamingEntityService
    {
        Task<PaginatedResult<GamingEntityListItem>> GetEntitiesAsync(GamingEntityFilter filter);
        Task<List<GamingEntityListItem>> GetHierarchicalEntitiesAsync();
        Task<List<GamingEntityListItem>> GetHierarchicalEntitiesLightAsync();
        Task<GamingEntityDetail?> GetEntityByIdAsync(string id);
        Task<GamingEntityHierarchy?> GetEntityHierarchyAsync(string id);
        Task<List<GamingEntityListItem>> GetChildrenAsync(string parentId, EntityRole parentRole);
        Task<GamingEntityDetail?> CreateEntityAsync(BloodSuckersSlot.Shared.Models.GamingEntity entity);
        Task<GamingEntityDetail?> UpdateEntityAsync(string id, BloodSuckersSlot.Shared.Models.GamingEntity entity);
        Task<bool> DeleteEntityAsync(string id);
        Task<bool> ToggleActiveAsync(string id);
        Task<List<string>> GetAvailableCurrenciesAsync();
        Task<Dictionary<string, int>> GetEntityStatsAsync();
    }

    public class GamingEntityService : IGamingEntityService
    {
        private readonly IMongoCollection<ApiGamingEntity> _collection;
        private readonly ILogger<GamingEntityService> _logger;

        public GamingEntityService(IMongoDatabase database, ILogger<GamingEntityService> logger)
        {
            _collection = database.GetCollection<ApiGamingEntity>("gamingEntities");
            _logger = logger;
            
            // Create indexes
            CreateIndexes();
        }

        private void CreateIndexes()
        {
            try
            {
                // Unique index on role + username
                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.Role).Ascending(x => x.Username),
                        new CreateIndexOptions { Unique = true }
                    )
                );

                // Indexes for hierarchical queries
                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.SuperAgentId)
                    )
                );

                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.AgentId)
                    )
                );

                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.TokenId)
                    )
                );

                // Compound index for role + parent relationships (optimized for hierarchical queries)
                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.Role)
                            .Ascending(x => x.SuperAgentId)
                            .Ascending(x => x.AgentId)
                            .Ascending(x => x.TokenId)
                    )
                );

                // Index for active status filtering
                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.Active)
                    )
                );

                // Compound index for filtering
                _collection.Indexes.CreateOne(
                    new CreateIndexModel<ApiGamingEntity>(
                        Builders<ApiGamingEntity>.IndexKeys.Ascending(x => x.Role).Ascending(x => x.Active)
                    )
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to create some indexes: {Error}", ex.Message);
            }
        }

        public async Task<PaginatedResult<GamingEntityListItem>> GetEntitiesAsync(GamingEntityFilter filter)
        {
            try
            {
                var filterDefinition = BuildFilter(filter);
                var sortDefinition = Builders<ApiGamingEntity>.Sort.Ascending(x => x.Role).Ascending(x => x.Username);

                var totalCount = await _collection.CountDocumentsAsync(filterDefinition);
                var totalPages = (int)Math.Ceiling((double)totalCount / filter.PageSize);

                var entities = await _collection
                    .Find(filterDefinition)
                    .Sort(sortDefinition)
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Limit(filter.PageSize)
                    .ToListAsync();

                var items = entities.Select(MapToListItem).ToList();

                return new PaginatedResult<GamingEntityListItem>
                {
                    Items = items,
                    TotalCount = (int)totalCount,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize,
                    TotalPages = totalPages
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities with filter: {@Filter}", filter);
                throw;
            }
        }

        public async Task<GamingEntityDetail?> GetEntityByIdAsync(string id)
        {
            try
            {
                var entity = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
                return entity != null ? MapToDetail(entity) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by id: {Id}", id);
                throw;
            }
        }

        public async Task<GamingEntityHierarchy?> GetEntityHierarchyAsync(string id)
        {
            try
            {
                var entity = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
                if (entity == null) return null;

                var hierarchy = new GamingEntityHierarchy
                {
                    Entity = MapToDetail(entity)
                };

                // Get parent entities
                if (!string.IsNullOrEmpty(entity.SuperAgentId))
                {
                    hierarchy.SuperAgent = await GetEntityByIdAsync(entity.SuperAgentId);
                }

                if (!string.IsNullOrEmpty(entity.AgentId))
                {
                    hierarchy.Agent = await GetEntityByIdAsync(entity.AgentId);
                }

                if (!string.IsNullOrEmpty(entity.TokenId))
                {
                    hierarchy.Token = await GetEntityByIdAsync(entity.TokenId);
                }

                // Get children
                var children = await GetChildrenAsync(id, entity.Role);
                var childDetails = new List<GamingEntityDetail>();
                foreach (var child in children)
                {
                    var childDetail = await GetEntityByIdAsync(child.Id);
                    if (childDetail != null)
                    {
                        childDetails.Add(childDetail);
                    }
                }
                hierarchy.Children = childDetails;

                return hierarchy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity hierarchy for id: {Id}", id);
                throw;
            }
        }

        public async Task<List<GamingEntityListItem>> GetHierarchicalEntitiesAsync()
        {
            try
            {
                // Minimal projection - only absolutely essential fields
                var projection = Builders<ApiGamingEntity>.Projection
                    .Include(x => x.Id)
                    .Include(x => x.Username)
                    .Include(x => x.Email)
                    .Include(x => x.Role)
                    .Include(x => x.SuperAgentId)
                    .Include(x => x.AgentId)
                    .Include(x => x.TokenId)
                    .Include(x => x.Active)
                    .Include(x => x.LastLoginDate);

                // Use index-optimized sort
                var sortDefinition = Builders<ApiGamingEntity>.Sort.Ascending(x => x.Role).Ascending(x => x.Username);

                var entities = await _collection
                    .Find(Builders<ApiGamingEntity>.Filter.Empty)
                    .Project<ApiGamingEntity>(projection)
                    .Sort(sortDefinition)
                    .ToListAsync();

                return entities.Select(MapToListItem).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting hierarchical entities");
                throw;
            }
        }

        public async Task<List<GamingEntityListItem>> GetHierarchicalEntitiesLightAsync()
        {
            try
            {
                // Ultra-minimal projection - only core fields for list display
                var projection = Builders<ApiGamingEntity>.Projection
                    .Include(x => x.Id)
                    .Include(x => x.Username)
                    .Include(x => x.Role)
                    .Include(x => x.SuperAgentId)
                    .Include(x => x.AgentId)
                    .Include(x => x.TokenId)
                    .Include(x => x.Active);

                // Use index-optimized sort
                var sortDefinition = Builders<ApiGamingEntity>.Sort.Ascending(x => x.Role).Ascending(x => x.Username);

                var entities = await _collection
                    .Find(Builders<ApiGamingEntity>.Filter.Empty)
                    .Project<ApiGamingEntity>(projection)
                    .Sort(sortDefinition)
                    .ToListAsync();

                return entities.Select(MapToListItemLight).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting hierarchical entities light");
                throw;
            }
        }

        public async Task<List<GamingEntityListItem>> GetChildrenAsync(string parentId, EntityRole parentRole)
        {
            try
            {
                FilterDefinition<ApiGamingEntity> filter;
                
                switch (parentRole)
                {
                    case EntityRole.SUPER_AGENT:
                        filter = Builders<ApiGamingEntity>.Filter.Eq(x => x.SuperAgentId, parentId);
                        break;
                    case EntityRole.AGENT:
                        filter = Builders<ApiGamingEntity>.Filter.Eq(x => x.AgentId, parentId);
                        break;
                    case EntityRole.TOKEN:
                        filter = Builders<ApiGamingEntity>.Filter.Eq(x => x.TokenId, parentId);
                        break;
                    default:
                        return new List<GamingEntityListItem>();
                }

                var entities = await _collection
                    .Find(filter)
                    .Sort(Builders<ApiGamingEntity>.Sort.Ascending(x => x.Role).Ascending(x => x.Username))
                    .ToListAsync();

                return entities.Select(MapToListItem).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting children for parent: {ParentId}, Role: {Role}", parentId, parentRole);
                throw;
            }
        }

        public async Task<GamingEntityDetail?> CreateEntityAsync(BloodSuckersSlot.Shared.Models.GamingEntity entity)
        {
            try
            {
                var apiEntity = ApiGamingEntity.FromShared(entity);
                apiEntity.CreatedAt = DateTime.UtcNow;
                apiEntity.UpdatedAt = DateTime.UtcNow;
                apiEntity.InsertDate = DateTime.UtcNow;

                // Validate hierarchy
                await ValidateHierarchy(apiEntity);

                await _collection.InsertOneAsync(apiEntity);
                return MapToDetail(apiEntity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity: {@Entity}", entity);
                throw;
            }
        }

        public async Task<GamingEntityDetail?> UpdateEntityAsync(string id, BloodSuckersSlot.Shared.Models.GamingEntity entity)
        {
            try
            {
                var apiEntity = ApiGamingEntity.FromShared(entity);
                apiEntity.Id = id;
                apiEntity.UpdatedAt = DateTime.UtcNow;

                // Validate hierarchy
                await ValidateHierarchy(apiEntity);

                var result = await _collection.ReplaceOneAsync(x => x.Id == id, apiEntity);
                
                if (result.ModifiedCount > 0)
                {
                    return MapToDetail(apiEntity);
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating entity: {Id}, {@Entity}", id, entity);
                throw;
            }
        }

        public async Task<bool> DeleteEntityAsync(string id)
        {
            try
            {
                // Check if entity has children
                var children = await GetChildrenAsync(id, EntityRole.SUPER_AGENT);
                if (children.Any())
                {
                    throw new InvalidOperationException("Cannot delete entity with children");
                }

                var result = await _collection.DeleteOneAsync(x => x.Id == id);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting entity: {Id}", id);
                throw;
            }
        }

        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                var entity = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
                if (entity == null) return false;

                entity.Active = !entity.Active;
                entity.UpdatedAt = DateTime.UtcNow;

                var result = await _collection.ReplaceOneAsync(x => x.Id == id, entity);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active status for entity: {Id}", id);
                throw;
            }
        }

        public async Task<List<string>> GetAvailableCurrenciesAsync()
        {
            try
            {
                var currencies = await _collection
                    .Distinct(x => x.Currency, x => !string.IsNullOrEmpty(x.Currency))
                    .ToListAsync();

                return currencies.Where(c => !string.IsNullOrEmpty(c)).OrderBy(c => c).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available currencies");
                throw;
            }
        }

        public async Task<Dictionary<string, int>> GetEntityStatsAsync()
        {
            try
            {
                var stats = new Dictionary<string, int>();

                // Count by role
                var roleCounts = await _collection
                    .Aggregate()
                    .Group(x => x.Role, g => new { Role = g.Key, Count = g.Count() })
                    .ToListAsync();

                foreach (var roleCount in roleCounts)
                {
                    stats[$"{roleCount.Role}_COUNT"] = roleCount.Count;
                }

                // Count active entities
                var activeCount = await _collection.CountDocumentsAsync(x => x.Active);
                stats["ACTIVE_COUNT"] = (int)activeCount;

                // Count inactive entities
                var inactiveCount = await _collection.CountDocumentsAsync(x => !x.Active);
                stats["INACTIVE_COUNT"] = (int)inactiveCount;

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity stats");
                throw;
            }
        }

        private FilterDefinition<ApiGamingEntity> BuildFilter(GamingEntityFilter filter)
        {
            var filters = new List<FilterDefinition<ApiGamingEntity>>();

            if (filter.Role.HasValue)
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Eq(x => x.Role, filter.Role.Value));
            }

            if (!string.IsNullOrEmpty(filter.SuperAgentId))
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Eq(x => x.SuperAgentId, filter.SuperAgentId));
            }

            if (!string.IsNullOrEmpty(filter.AgentId))
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Eq(x => x.AgentId, filter.AgentId));
            }

            if (!string.IsNullOrEmpty(filter.TokenId))
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Eq(x => x.TokenId, filter.TokenId));
            }

            if (filter.Active.HasValue)
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Eq(x => x.Active, filter.Active.Value));
            }

            if (!string.IsNullOrEmpty(filter.Currency))
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Eq(x => x.Currency, filter.Currency));
            }

            if (filter.MinRtp.HasValue)
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Gte(x => x.Rtp, filter.MinRtp.Value));
            }

            if (filter.MaxRtp.HasValue)
            {
                filters.Add(Builders<ApiGamingEntity>.Filter.Lte(x => x.Rtp, filter.MaxRtp.Value));
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var searchFilter = Builders<ApiGamingEntity>.Filter.Or(
                    Builders<ApiGamingEntity>.Filter.Regex(x => x.Username, new BsonRegularExpression(filter.SearchTerm, "i")),
                    Builders<ApiGamingEntity>.Filter.Regex(x => x.Email, new BsonRegularExpression(filter.SearchTerm, "i")),
                    Builders<ApiGamingEntity>.Filter.Regex(x => x.ClientName, new BsonRegularExpression(filter.SearchTerm, "i")),
                    Builders<ApiGamingEntity>.Filter.Regex(x => x.ShopName, new BsonRegularExpression(filter.SearchTerm, "i"))
                );
                filters.Add(searchFilter);
            }

            return filters.Count > 0 
                ? Builders<ApiGamingEntity>.Filter.And(filters) 
                : Builders<ApiGamingEntity>.Filter.Empty;
        }

        private async Task ValidateHierarchy(ApiGamingEntity entity)
        {
            // Validate parent references exist
            if (!string.IsNullOrEmpty(entity.SuperAgentId))
            {
                var superAgent = await _collection.Find(x => x.Id == entity.SuperAgentId).FirstOrDefaultAsync();
                if (superAgent == null || superAgent.Role != EntityRole.SUPER_AGENT)
                {
                    throw new ArgumentException("Invalid SuperAgent reference");
                }
            }

            if (!string.IsNullOrEmpty(entity.AgentId))
            {
                var agent = await _collection.Find(x => x.Id == entity.AgentId).FirstOrDefaultAsync();
                if (agent == null || agent.Role != EntityRole.AGENT)
                {
                    throw new ArgumentException("Invalid Agent reference");
                }
            }

            if (!string.IsNullOrEmpty(entity.TokenId))
            {
                var token = await _collection.Find(x => x.Id == entity.TokenId).FirstOrDefaultAsync();
                if (token == null || token.Role != EntityRole.TOKEN)
                {
                    throw new ArgumentException("Invalid Token reference");
                }
            }

            // Validate RTP hierarchy
            if (entity.Role == EntityRole.GROUP && entity.Rtp.HasValue)
            {
                await ValidateRtpHierarchy(entity);
            }
        }

        private async Task ValidateRtpHierarchy(ApiGamingEntity group)
        {
            if (string.IsNullOrEmpty(group.TokenId)) return;

            var token = await _collection.Find(x => x.Id == group.TokenId).FirstOrDefaultAsync();
            if (token != null && token.NetworkProfitPercent.HasValue)
            {
                if (group.Rtp > token.NetworkProfitPercent)
                {
                    throw new ArgumentException("Group RTP cannot exceed Token RTP");
                }
            }
        }

        private GamingEntityListItem MapToListItem(ApiGamingEntity entity)
        {
            return new GamingEntityListItem
            {
                Id = entity.Id,
                Username = entity.Username,
                Role = entity.Role,
                SuperAgentId = entity.SuperAgentId,
                AgentId = entity.AgentId,
                TokenId = entity.TokenId,
                Email = entity.Email,
                Active = entity.Active,
                LastLoginDate = entity.LastLoginDate,
                CreatedAt = entity.CreatedAt
            };
        }

        private GamingEntityListItem MapToListItemLight(ApiGamingEntity entity)
        {
            return new GamingEntityListItem
            {
                Id = entity.Id,
                Username = entity.Username,
                Role = entity.Role,
                SuperAgentId = entity.SuperAgentId,
                AgentId = entity.AgentId,
                TokenId = entity.TokenId,
                Email = string.Empty, // Not loaded in light version
                Active = entity.Active,
                LastLoginDate = null, // Not loaded in light version
                CreatedAt = DateTime.MinValue // Not loaded in light version
            };
        }

        private GamingEntityDetail MapToDetail(ApiGamingEntity entity)
        {
            return new GamingEntityDetail
            {
                Id = entity.Id,
                Username = entity.Username,
                Role = entity.Role,
                SuperAgentId = entity.SuperAgentId,
                AgentId = entity.AgentId,
                TokenId = entity.TokenId,
                Email = entity.Email,
                Active = entity.Active,
                LastLoginDate = entity.LastLoginDate,
                CreatedAt = entity.CreatedAt,
                GameProviderProfit = entity.GameProviderProfit,
                NetworkProfitPercent = entity.NetworkProfitPercent,
                SubsidiaryName = entity.SubsidiaryName,
                Region = entity.Region,
                ClientName = entity.ClientName,
                ClientType = entity.ClientType,
                Endpoint = entity.Endpoint,
                PublicKey = entity.PublicKey,
                TokenActive = entity.TokenActive,
                ApiConfig = entity.ApiConfig,
                Currency = entity.Currency,
                TemplateGameLimit = entity.TemplateGameLimit,
                Rtp = entity.Rtp,
                GroupReference = entity.GroupReference,
                ShopName = entity.ShopName,
                ShopType = entity.ShopType,
                GameLimits = entity.GameLimits,
                TokenEndpoint = entity.TokenEndpoint,
                TokenPublicKey = entity.TokenPublicKey,
                InsertDate = entity.InsertDate,
                UpdatedAt = entity.UpdatedAt
            };
        }
    }
}
