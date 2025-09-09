using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using BloodSuckersSlot.Shared.Models;

namespace BloodSuckersSlot.Api.Models
{
    [BsonIgnoreExtraElements]
    public class GamingEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        
        [BsonElement("username")]
        public string Username { get; set; } = string.Empty;
        
        [BsonElement("role")]
        public EntityRole Role { get; set; }
        
        [BsonElement("superAgentId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? SuperAgentId { get; set; }
        
        [BsonElement("agentId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? AgentId { get; set; }
        
        [BsonElement("tokenId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? TokenId { get; set; }
        
        [BsonElement("groupId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? GroupId { get; set; }
        
        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;
        
        [BsonElement("passwordHash")]
        public string? PasswordHash { get; set; }
        
        [BsonElement("active")]
        public bool Active { get; set; } = true;
        
        [BsonElement("gameProviderProfit")]
        public GameProviderProfit? GameProviderProfit { get; set; }
        
        [BsonElement("networkProfitPercent")]
        public int? NetworkProfitPercent { get; set; }
        
        [BsonElement("subsidiaryName")]
        public string? SubsidiaryName { get; set; }
        
        [BsonElement("region")]
        public string? Region { get; set; }
        
        [BsonElement("clientName")]
        public string? ClientName { get; set; }
        
        [BsonElement("clientType")]
        public string? ClientType { get; set; }
        
        [BsonElement("endpoint")]
        public string? Endpoint { get; set; }
        
        [BsonElement("publicKey")]
        public string? PublicKey { get; set; }
        
        [BsonElement("tokenActive")]
        public bool? TokenActive { get; set; }
        
        [BsonElement("apiConfig")]
        public ApiConfig? ApiConfig { get; set; }
        
        [BsonElement("currency")]
        public string? Currency { get; set; }
        
        [BsonElement("templateGameLimit")]
        public string? TemplateGameLimit { get; set; }
        
        [BsonElement("rtp")]
        public int? Rtp { get; set; }
        
        [BsonElement("groupReference")]
        public string? GroupReference { get; set; }
        
        [BsonElement("shopName")]
        public string? ShopName { get; set; }
        
        [BsonElement("shopType")]
        public string? ShopType { get; set; }
        
        [BsonElement("gameLimits")]
        public GameLimits? GameLimits { get; set; }
        
        [BsonElement("tokenEndpoint")]
        public string? TokenEndpoint { get; set; }
        
        [BsonElement("tokenPublicKey")]
        public string? TokenPublicKey { get; set; }
        
        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("lastLoginDate")]
        public DateTime? LastLoginDate { get; set; }
        
        [BsonElement("insertDate")]
        public DateTime? InsertDate { get; set; }

        // Conversion methods to/from shared model
        public static GamingEntity FromShared(BloodSuckersSlot.Shared.Models.GamingEntity shared)
        {
            return new GamingEntity
            {
                Id = shared.Id,
                Username = shared.Username,
                Role = shared.Role,
                SuperAgentId = shared.SuperAgentId,
                AgentId = shared.AgentId,
                TokenId = shared.TokenId,
                GroupId = shared.GroupId,
                Email = shared.Email,
                PasswordHash = shared.PasswordHash,
                Active = shared.Active,
                GameProviderProfit = shared.GameProviderProfit,
                NetworkProfitPercent = shared.NetworkProfitPercent,
                SubsidiaryName = shared.SubsidiaryName,
                Region = shared.Region,
                ClientName = shared.ClientName,
                ClientType = shared.ClientType,
                Endpoint = shared.Endpoint,
                PublicKey = shared.PublicKey,
                TokenActive = shared.TokenActive,
                ApiConfig = shared.ApiConfig,
                Currency = shared.Currency,
                TemplateGameLimit = shared.TemplateGameLimit,
                Rtp = shared.Rtp,
                GroupReference = shared.GroupReference,
                ShopName = shared.ShopName,
                ShopType = shared.ShopType,
                GameLimits = shared.GameLimits,
                TokenEndpoint = shared.TokenEndpoint,
                TokenPublicKey = shared.TokenPublicKey,
                CreatedAt = shared.CreatedAt,
                UpdatedAt = shared.UpdatedAt,
                LastLoginDate = shared.LastLoginDate,
                InsertDate = shared.InsertDate
            };
        }

        public BloodSuckersSlot.Shared.Models.GamingEntity ToShared()
        {
            return new BloodSuckersSlot.Shared.Models.GamingEntity
            {
                Id = Id,
                Username = Username,
                Role = Role,
                SuperAgentId = SuperAgentId,
                AgentId = AgentId,
                TokenId = TokenId,
                GroupId = GroupId,
                Email = Email,
                PasswordHash = PasswordHash,
                Active = Active,
                GameProviderProfit = GameProviderProfit,
                NetworkProfitPercent = NetworkProfitPercent,
                SubsidiaryName = SubsidiaryName,
                Region = Region,
                ClientName = ClientName,
                ClientType = ClientType,
                Endpoint = Endpoint,
                PublicKey = PublicKey,
                TokenActive = TokenActive,
                ApiConfig = ApiConfig,
                Currency = Currency,
                TemplateGameLimit = TemplateGameLimit,
                Rtp = Rtp,
                GroupReference = GroupReference,
                ShopName = ShopName,
                ShopType = ShopType,
                GameLimits = GameLimits,
                TokenEndpoint = TokenEndpoint,
                TokenPublicKey = TokenPublicKey,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                LastLoginDate = LastLoginDate,
                InsertDate = InsertDate
            };
        }
    }
}
