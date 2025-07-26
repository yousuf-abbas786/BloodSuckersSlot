using Microsoft.AspNetCore.SignalR;
using Shared;
using System.Threading.Tasks;
using System;

namespace BloodSuckersSlot.Api
{
    public class RtpHub : Hub
    {
        public async Task BroadcastRtpUpdate(RtpUpdate update)
        {
            Console.WriteLine($"[API] Received update: Spin {update.SpinNumber}, RTP {update.ActualRtp}, HitRate {update.ActualHitRate}");
            Console.WriteLine($"[API] Performance: SpinTime={update.SpinTimeSeconds}, AvgTime={update.AverageSpinTimeSeconds}, TotalSpins={update.TotalSpins}");
            Console.WriteLine($"[API] Reel Sets: High={update.HighRtpSetCount}, Mid={update.MidRtpSetCount}, Low={update.LowRtpSetCount}, Fallback={update.SafeFallbackCount}");
            Console.WriteLine($"[API] Full update object: {System.Text.Json.JsonSerializer.Serialize(update)}");
            await Clients.All.SendAsync("ReceiveRtpUpdate", update);
        }
    }
} 