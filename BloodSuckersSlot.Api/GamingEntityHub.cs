using Microsoft.AspNetCore.SignalR;
using BloodSuckersSlot.Shared.Models;

namespace BloodSuckersSlot.Api
{
    public class GamingEntityHub : Hub
    {
        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task SubscribeToEntity(string entityId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"entity_{entityId}");
        }

        public async Task UnsubscribeFromEntity(string entityId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"entity_{entityId}");
        }

        public async Task SubscribeToRole(EntityRole role)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"role_{role}");
        }

        public async Task UnsubscribeFromRole(EntityRole role)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"role_{role}");
        }
    }

    public static class GamingEntityHubExtensions
    {
        public static async Task NotifyEntityCreated(this IHubContext<GamingEntityHub> hubContext, GamingEntityDetail entity)
        {
            await hubContext.Clients.All.SendAsync("EntityCreated", entity);
            await hubContext.Clients.Group($"role_{entity.Role}").SendAsync("EntityCreated", entity);
        }

        public static async Task NotifyEntityUpdated(this IHubContext<GamingEntityHub> hubContext, GamingEntityDetail entity)
        {
            await hubContext.Clients.All.SendAsync("EntityUpdated", entity);
            await hubContext.Clients.Group($"role_{entity.Role}").SendAsync("EntityUpdated", entity);
            await hubContext.Clients.Group($"entity_{entity.Id}").SendAsync("EntityUpdated", entity);
        }

        public static async Task NotifyEntityDeleted(this IHubContext<GamingEntityHub> hubContext, string entityId, EntityRole role)
        {
            await hubContext.Clients.All.SendAsync("EntityDeleted", entityId);
            await hubContext.Clients.Group($"role_{role}").SendAsync("EntityDeleted", entityId);
            await hubContext.Clients.Group($"entity_{entityId}").SendAsync("EntityDeleted", entityId);
        }

        public static async Task NotifyEntityStatusChanged(this IHubContext<GamingEntityHub> hubContext, string entityId, EntityRole role, bool isActive)
        {
            await hubContext.Clients.All.SendAsync("EntityStatusChanged", entityId, isActive);
            await hubContext.Clients.Group($"role_{role}").SendAsync("EntityStatusChanged", entityId, isActive);
            await hubContext.Clients.Group($"entity_{entityId}").SendAsync("EntityStatusChanged", entityId, isActive);
        }
    }
}
