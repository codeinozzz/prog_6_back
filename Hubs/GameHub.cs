using Microsoft.AspNetCore.SignalR;

namespace BattleTanks_Backend.Hubs;

public class GameHub : Hub
{
    private static readonly Dictionary<string, string> ConnectedPlayers = new();

    public async Task SendPlayerMove(string playerId, object movement)
    {
        await Clients.Others.SendAsync("ReceivePlayerMove", playerId, movement);
    }

    public async Task SendChatMessage(string sender, string message)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Console.WriteLine($"[GameHub] ChatMessage from {sender} at {timestamp}");
        await Clients.All.SendAsync("ReceiveChatMessage", sender, message);
    }

    public async Task StartGame(string mapName)
    {
        Console.WriteLine($"[GameHub] Game started with map: {mapName}");
        await Clients.All.SendAsync("GameStarted", mapName);
    }

    public async Task JoinGame(string playerId, string playerName)
    {
        ConnectedPlayers[Context.ConnectionId] = playerName;
        Console.WriteLine($"[GameHub] Player joined: {playerName} ({playerId})");
        await Clients.All.SendAsync("PlayerJoined", playerId, playerName);
    }

    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"[GameHub] Client connected: {Context.ConnectionId}");
        await Clients.Caller.SendAsync("ConnectionEstablished", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectedPlayers.Remove(Context.ConnectionId);
        Console.WriteLine($"[GameHub] Client disconnected: {Context.ConnectionId}");
        await Clients.Others.SendAsync("PlayerLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
