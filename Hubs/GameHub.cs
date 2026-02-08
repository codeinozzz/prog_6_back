using Microsoft.AspNetCore.SignalR;

namespace BattleTanks_Backend.Hubs;

public class GameHub : Hub
{
    public async Task SendPlayerMove(string PlayerId,object movement)
    {
        
        await Clients.Others.SendAsync("ReceivePlayerMove",PlayerId, movement);
    }

    public async Task SendChatMessage(string sender, string message)
    {
        await Clients.All.SendAsync("ReceiveChatMessage", sender, message);
    }

    public async Task JoinGame(string playerId, string playerName)
    {
        await Clients.All.SendAsync("PlayerJoined", playerId, playerName);
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("ConnectionEstablished", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.Others.SendAsync("PlayerLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

}