using BloodSuckersSlot.Api;
using BloodSuckersSlot.Api.Models;
using BloodSuckersSlot.Api.Services;
using BloodSuckersSlot.Api.Controllers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Configure services
var configuration = builder.Configuration;
var environment = builder.Environment;

// Bind configuration sections
var apiSettings = new ApiSettings();
var performanceSettings = new PerformanceSettings();
var mongoDbSettings = new MongoDbSettings();

configuration.GetSection("ApiSettings").Bind(apiSettings);
configuration.GetSection("Performance").Bind(performanceSettings);
configuration.GetSection("MongoDb").Bind(mongoDbSettings);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add response caching
builder.Services.AddResponseCaching();

// Configure Swagger based on environment
if (apiSettings.EnableSwagger)
{
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo 
        { 
            Title = "BloodSuckers Slot API", 
            Version = "v1",
            Description = $"Environment: {configuration["Environment"]} | API for slot game with lazy loading optimization"
        });
    });
}

builder.Services.AddSignalR();

// Configure CORS based on settings
if (apiSettings.EnableCors)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            var origins = apiSettings.CorsOrigins.Length > 0 
                ? apiSettings.CorsOrigins 
                : new[] { "http://localhost:3000", "http://localhost:5000", "http://localhost:8080" };
            
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });
}

// Register configuration objects
builder.Services.AddSingleton(apiSettings);
builder.Services.AddSingleton(performanceSettings);
builder.Services.AddSingleton(mongoDbSettings);

// Register MongoDB services
builder.Services.AddSingleton<IMongoClient>(provider =>
{
    var settings = provider.GetRequiredService<MongoDbSettings>();
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(provider =>
{
    var client = provider.GetRequiredService<IMongoClient>();
    var settings = provider.GetRequiredService<MongoDbSettings>();
    return client.GetDatabase(settings.Database);
});

// Configure JWT authentication
var jwtSecretKey = configuration["Jwt:SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
var jwtIssuer = configuration["Jwt:Issuer"] ?? "BloodSuckersSlot";
var jwtAudience = configuration["Jwt:Audience"] ?? "BloodSuckersSlotUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
    options.AddPolicy("PlayerOrAdmin", policy => policy.RequireRole("ADMIN", "PLAYER"));
});

// Register services
builder.Services.AddScoped<IGamingEntityService, GamingEntityService>();
builder.Services.AddScoped<IGamingEntityAuthService, GamingEntityAuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPlayerSessionService, PlayerSessionService>();
builder.Services.AddSingleton<IPlayerSpinSessionService, PlayerSpinSessionService>();
builder.Services.AddScoped<SpinLogicHelper>();

// 🚀 CRITICAL FIX: Register ReelSetCacheService as Singleton for performance
builder.Services.AddSingleton<IReelSetCacheService, ReelSetCacheService>();

// 🚀 CRITICAL FIX: Register SpinController as Scoped but optimize constructor
builder.Services.AddScoped<SpinController>();

// Register AutoSpinService as both a service and a background service
builder.Services.AddSingleton<AutoSpinService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<AutoSpinService>());

// Register PlayerSessionCleanupService
builder.Services.AddHostedService<PlayerSessionCleanupService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (apiSettings.EnableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BloodSuckers Slot API v1");
        c.RoutePrefix = "swagger";
    });
}

// Configure error handling based on environment
if (environment.IsDevelopment() || apiSettings.EnableDetailedErrors)
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Remove HTTPS redirection for development/testing
// app.UseHttpsRedirection();

if (apiSettings.EnableCors)
{
    app.UseCors();
}

// Add response caching middleware
app.UseResponseCaching();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Register the SignalR hub endpoints
app.MapHub<RtpHub>("/rtpHub");
app.MapHub<GamingEntityHub>("/gamingEntityHub");

// Create default admin entity on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var entityAuthService = scope.ServiceProvider.GetRequiredService<IGamingEntityAuthService>();
        await entityAuthService.CreateDefaultAdminAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error creating default admin entity");
    }
}

// Add health check endpoint
app.MapGet("/health", () => new 
{
    Status = "Healthy",
    Environment = configuration["Environment"],
    Timestamp = DateTime.UtcNow,
    Version = "1.0.0",
    Features = new
    {
        LazyLoading = true,
        Caching = true,
        Prefetching = true,
        Swagger = apiSettings.EnableSwagger,
        Cors = apiSettings.EnableCors
    }
});

// Add configuration info endpoint (for debugging)
if (environment.IsDevelopment())
{
    app.MapGet("/config", () => new
    {
        Environment = configuration["Environment"],
        ApiSettings = apiSettings,
        PerformanceSettings = performanceSettings,
        MongoDbSettings = new
        {
            Database = mongoDbSettings.Database,
            ConnectionTimeout = mongoDbSettings.ConnectionTimeout,
            MaxPoolSize = mongoDbSettings.MaxPoolSize
        }
    });
}

app.Run();
