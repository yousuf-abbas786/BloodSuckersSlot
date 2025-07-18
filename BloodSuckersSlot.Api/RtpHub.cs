using Microsoft.AspNetCore.SignalR;
using BloodSuckersSlot.Api.Models;
using System.Threading.Tasks;
using System;

namespace BloodSuckersSlot.Api
{
    public class RtpHub : Hub
    {
        public async Task BroadcastRtpUpdate(RtpUpdate update)
        {
            Console.WriteLine($"[API] Received update: Spin {update.SpinNumber}, RTP {update.ActualRtp}, HitRate {update.ActualHitRate}, TargetRTP {update.TargetRtp}, TargetHitRate {update.TargetHitRate}, Timestamp {update.Timestamp}");
            await Clients.All.SendAsync("ReceiveRtpUpdate", update);
        }
    }
} 