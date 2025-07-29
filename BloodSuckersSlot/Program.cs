using BloodSuckersSlot;
using Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Load GameConfig from appsettings
var config = GameConfigLoader.LoadFromConfiguration(builder.Configuration);
config.PrintConfiguration();

// Validate configuration
if (!config.Validate())
{
    Console.WriteLine("Configuration validation failed. Exiting.");
    return;
}

builder.Services.AddSingleton(config);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
