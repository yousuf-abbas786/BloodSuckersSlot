using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BloodSuckersSlot.Web;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Icons.FontAwesome;
using Blazorise.Charts;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Load configuration from appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Services
    .AddBlazorise(options => { options.Immediate = true; })
    .AddBootstrapProviders()
    .AddFontAwesomeIcons();

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient with API base URL from configuration
builder.Services.AddScoped(sp => 
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var apiBaseUrl = configuration["ApiBaseUrl"] ?? "/api";
    
    // If it's a relative URL, use the host environment base address
    if (apiBaseUrl.StartsWith("/"))
    {
        return new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    }
    else
    {
        return new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
    }
});

builder.Services.AddScoped<BloodSuckersSlot.Web.Services.RtpSignalRService>();
builder.Services.AddScoped<BloodSuckersSlot.Web.Services.MongoDbService>();
builder.Services.AddScoped<BloodSuckersSlot.Web.Services.IAuthService, BloodSuckersSlot.Web.Services.AuthService>();
builder.Services.AddScoped<BloodSuckersSlot.Web.Services.IGamingEntityService, BloodSuckersSlot.Web.Services.GamingEntityService>();

await builder.Build().RunAsync();
