using System.ComponentModel.DataAnnotations;

namespace BloodSuckersSlot.Shared.Models
{
    public enum EntityRole
    {
        SUPER_AGENT,
        AGENT,
        TOKEN,
        GROUP
    }

    public class GameProviderProfit
    {
        [Range(0, 100)]
        public int Rgs { get; set; }

        [Range(0, 100)]
        public int Spribeplay { get; set; }

        [Range(0, 100)]
        public int Pragmatic { get; set; }

        [Range(0, 100)]
        public int Evolution { get; set; }
    }

    public class ApiConfig
    {
        public int Timeout { get; set; } = 30000;

        public int RetryAttempts { get; set; } = 3;

        [Url]
        public string? WebhookUrl { get; set; }
    }

    public class GameLimits
    {
        [Range(0, double.MaxValue)]
        public decimal MaxBet { get; set; }

        [Range(0, double.MaxValue)]
        public decimal MinBet { get; set; }

        [Range(0, double.MaxValue)]
        public decimal DailyLimit { get; set; }

        [Range(0, double.MaxValue)]
        public decimal WeeklyLimit { get; set; }
    }

    // DTOs for API responses
    public class GamingEntityListItem
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public EntityRole Role { get; set; }
        public string? SuperAgentId { get; set; }
        public string? AgentId { get; set; }
        public string? TokenId { get; set; }
        public string Email { get; set; } = string.Empty;
        public bool Active { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class GamingEntityDetail : GamingEntityListItem
    {
        public GameProviderProfit? GameProviderProfit { get; set; }
        public int? NetworkProfitPercent { get; set; }
        public string? SubsidiaryName { get; set; }
        public string? Region { get; set; }
        public string? ClientName { get; set; }
        public string? ClientType { get; set; }
        public string? Endpoint { get; set; }
        public string? PublicKey { get; set; }
        public bool? TokenActive { get; set; }
        public ApiConfig? ApiConfig { get; set; }
        public string? Currency { get; set; }
        public string? TemplateGameLimit { get; set; }
        public int? Rtp { get; set; }
        public string? GroupReference { get; set; }
        public string? ShopName { get; set; }
        public string? ShopType { get; set; }
        public GameLimits? GameLimits { get; set; }
        public string? TokenEndpoint { get; set; }
        public string? TokenPublicKey { get; set; }
        public DateTime? InsertDate { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class GamingEntityHierarchy
    {
        public GamingEntityDetail Entity { get; set; } = new();
        public GamingEntityDetail? SuperAgent { get; set; }
        public GamingEntityDetail? Agent { get; set; }
        public GamingEntityDetail? Token { get; set; }
        public List<GamingEntityDetail> Children { get; set; } = new();
    }

    public class PaginatedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class GamingEntityFilter
    {
        public EntityRole? Role { get; set; }
        public string? SuperAgentId { get; set; }
        public string? AgentId { get; set; }
        public string? TokenId { get; set; }
        public bool? Active { get; set; }
        public string? SearchTerm { get; set; }
        public string? Currency { get; set; }
        public int? MinRtp { get; set; }
        public int? MaxRtp { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class GamingEntity
    {
        public virtual string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public EntityRole Role { get; set; }
        public string? SuperAgentId { get; set; }
        public string? AgentId { get; set; }
        public string? TokenId { get; set; }
        public string Email { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
        public GameProviderProfit? GameProviderProfit { get; set; }
        public int? NetworkProfitPercent { get; set; }
        public string? SubsidiaryName { get; set; }
        public string? Region { get; set; }
        public string? ClientName { get; set; }
        public string? ClientType { get; set; }
        public string? Endpoint { get; set; }
        public string? PublicKey { get; set; }
        public bool? TokenActive { get; set; }
        public ApiConfig? ApiConfig { get; set; }
        public string? Currency { get; set; }
        public string? TemplateGameLimit { get; set; }
        public int? Rtp { get; set; }
        public string? GroupReference { get; set; }
        public string? ShopName { get; set; }
        public string? ShopType { get; set; }
        public GameLimits? GameLimits { get; set; }
        public string? TokenEndpoint { get; set; }
        public string? TokenPublicKey { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginDate { get; set; }
        public DateTime? InsertDate { get; set; }
    }
}
