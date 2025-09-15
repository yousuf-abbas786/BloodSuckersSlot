using BloodSuckersSlot.Web.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace BloodSuckersSlot.Web.Services
{
    public class RtpSignalRService : IAsyncDisposable
    {
        private HubConnection? _hubConnection;
        private readonly string _hubUrl;
        
        public event Action<RtpUpdate>? OnRtpUpdate;

        public RtpSignalRService(IConfiguration configuration)
        {
            var apiBaseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5000";
            _hubUrl = $"{apiBaseUrl}/rtpHub";
            Console.WriteLine($"[SignalR] Initializing connection to {_hubUrl}");
        }

        public async Task StartAsync()
        {
            try
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(_hubUrl)
                    .Build();

                // Register the ReceiveRtpUpdate handler
                _hubConnection.On<string>("ReceiveRtpUpdate", OnReceiveRtpUpdate);

                await _hubConnection.StartAsync();
                Console.WriteLine($"[SignalR] Connected to hub at {_hubUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Failed to connect: {ex.Message}");
            }
        }

        private void OnReceiveRtpUpdate(string rtpUpdateJson)
        {
            try
            {
                var rtpUpdate = JsonSerializer.Deserialize<RtpUpdate>(rtpUpdateJson);
                if (rtpUpdate != null)
                {
                    Console.WriteLine($"[SignalR] Received RtpUpdate: TotalFreeSpinsAwarded={rtpUpdate.TotalFreeSpinsAwarded}, TotalBonusesTriggered={rtpUpdate.TotalBonusesTriggered}");
                    OnRtpUpdate?.Invoke(rtpUpdate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Error processing RtpUpdate: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
                Console.WriteLine("[SignalR] Disconnected from hub");
            }
        }
    }
}