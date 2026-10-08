using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace WebApplication24.Hubs;

public class ChatHub : Hub
{
    public static string UserGroup(int id) => $"user:{id}";

    public override async Task OnConnectedAsync()
    {
        var id = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is not null) await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(int.Parse(id)));
        await base.OnConnectedAsync();
    }
}
