using System.ComponentModel.DataAnnotations;

namespace BloodSuckersSlot.Api.Models
{
    public class ApiSettings
    {
        public bool EnableCors { get; set; } = true;
        public string[] CorsOrigins { get; set; } = new string[0];
        public int RequestTimeoutSeconds { get; set; } = 30;
        public bool EnableSwagger { get; set; } = false;
        public bool EnableDetailedErrors { get; set; } = false;
    }

    public class PerformanceSettings
    {
        public int MaxCacheSize { get; set; } = 10000;
        public int MaxReelSetsPerRange { get; set; } = 1000;
        public int PrefetchRangeCount { get; set; } = 5;
        public double PrefetchRangeSize { get; set; } = 0.1;
        public int PrefetchIntervalSeconds { get; set; } = 30;
        public int SpinTimeoutSeconds { get; set; } = 10;
    }

    public class MongoDbSettings
    {
        [Required]
        public string ConnectionString { get; set; } = string.Empty;
        
        [Required]
        public string Database { get; set; } = string.Empty;
        
        public int ConnectionTimeout { get; set; } = 30;
        public int MaxPoolSize { get; set; } = 100;
    }
}
