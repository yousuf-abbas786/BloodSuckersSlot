using BloodSuckersSlot;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Services.Configure<GameConfig>(builder.Configuration.GetSection("GameConfig"));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
