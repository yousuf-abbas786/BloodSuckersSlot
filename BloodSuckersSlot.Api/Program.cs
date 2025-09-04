using BloodSuckersSlot.Api;
using BloodSuckersSlot.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;

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

app.MapControllers();

// Register the SignalR hub endpoint
app.MapHub<RtpHub>("/rtpHub");

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
