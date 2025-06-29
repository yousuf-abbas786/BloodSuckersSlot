using BloodSuckersSlot;
using BloodSuckersSlot.Mongo;

using MongoDB.Driver;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Services.Configure<GameConfig>(builder.Configuration.GetSection("GameConfig"));



builder.Services.AddHostedService<Worker>();
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.AddSingleton<MongoServiceLoader>();
builder.Services.AddSingleton<ConfigManager>();

using var scope = builder.Services.BuildServiceProvider().CreateScope();
var configManager = scope.ServiceProvider.GetRequiredService<ConfigManager>();

var globalConfig = await configManager.GetGlobalConfigAsync();
var shopConfig = await configManager.GetShopConfigAsync("my-shop-id");
var playerConfig = await configManager.GetPlayerConfigAsync("my-player-id");

var playerSession = new PlayerSession
{
    PlayerId = playerConfig.PlayerId,
    ShopId = shopConfig.ShopId,
    Balance = playerConfig.Balance
};

var shopState = new ShopState
{
    ShopId = shopConfig.ShopId,
    Currency = shopConfig.Currency,
    Balance = shopConfig.InitialBalance
};


builder.Services.AddSingleton(globalConfig);
builder.Services.AddSingleton(shopConfig);
builder.Services.AddSingleton(playerSession);
builder.Services.AddSingleton(shopState);
builder.Services.AddSingleton<EngineConfig>(shopConfig.EngineConfig); // if it's nested


var host = builder.Build();
host.Run();
