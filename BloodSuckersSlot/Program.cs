using BloodSuckersSlot;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Use the new configuration system
var config = ConfigUtility.CreateConfiguration("balanced"); // Default to balanced preset
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
