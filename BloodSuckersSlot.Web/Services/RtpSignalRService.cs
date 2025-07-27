using BloodSuckersSlot.Web.Models;
using Microsoft.Extensions.Configuration;

namespace BloodSuckersSlot.Web.Services
{
    public class RtpSignalRService
    {
        public event Action<RtpUpdate>? OnRtpUpdate;

        public RtpSignalRService(IConfiguration configuration)
        {
            Console.WriteLine($"[SignalR] SignalR disabled for compatibility");
        }

        public async Task StartAsync()
        {
            // SignalR disabled
            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            // SignalR disabled
            await Task.CompletedTask;
        }
    }
}