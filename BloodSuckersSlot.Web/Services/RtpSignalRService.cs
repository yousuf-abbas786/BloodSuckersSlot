using Microsoft.AspNetCore.SignalR.Client;
using BloodSuckersSlot.Web.Models;
using Microsoft.Extensions.Configuration;

namespace BloodSuckersSlot.Web.Services
{
    public class RtpSignalRService : IAsyncDisposable
    {
        private readonly HubConnection _hubConnection;
        public event Action<RtpUpdate>? OnRtpUpdate;

        public RtpSignalRService(IConfiguration configuration)
        {
            var apiBaseUrl = configuration["ApiBaseUrl"] ?? "http://37.27.71.156:5000";
            Console.WriteLine($"[SignalR] Using API base URL: {apiBaseUrl}");
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{apiBaseUrl}/rtpHub")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<RtpUpdate>("ReceiveRtpUpdate", (update) =>
            {
                Console.WriteLine($"[SignalR] Received update: Spin={update.SpinNumber}, RTP={update.ActualRtp}");
                OnRtpUpdate?.Invoke(update);
            });
        }

        public async Task StartAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
                await _hubConnection.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _hubConnection.DisposeAsync();
        }
    }
}