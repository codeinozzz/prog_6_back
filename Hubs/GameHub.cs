using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.Models;

namespace BattleTanks_Backend.Hubs;

public record PlayerConnection(string PlayerName, string RoomId, double X = 0, double Y = 0);

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, PlayerConnection> ConnectedPlayers = new();

    private readonly BattleTanksDbContext _context;
    private readonly IDatabase _redis;

    public GameHub(BattleTanksDbContext context, IConnectionMultiplexer redis)
    {
        _context = context;
        _redis = redis.GetDatabase();
    }

    public async Task JoinGame(string playerId, string playerName, string roomId, double x = 0, double y = 0)
    {
        ConnectedPlayers[Context.ConnectionId] = new PlayerConnection(playerName, roomId, x, y);

        var groupName = $"room-{roomId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        // Send existing players in this room to the caller
        var existingPlayers = ConnectedPlayers
            .Where(kvp => kvp.Key != Context.ConnectionId && kvp.Value.RoomId == roomId)
            .Select(kvp => new { playerId = kvp.Key, playerName = kvp.Value.PlayerName, x = kvp.Value.X, y = kvp.Value.Y })
            .ToList();

        await Clients.Caller.SendAsync("ExistingPlayers", existingPlayers);

        Console.WriteLine($"[GameHub] Player joined room {roomId}: {playerName} ({playerId})");
        await Clients.OthersInGroup(groupName).SendAsync("PlayerJoined", playerId, playerName, x, y);

        // Cache en Redis: incrementar contador de jugadores por sala y guardar nombre del jugador
        try
        {
            await _redis.HashSetAsync($"room:{roomId}:players", Context.ConnectionId, playerName);
            await _redis.StringIncrementAsync($"room:{roomId}:count");
        }
        catch { /* Redis no disponible */ }
    }

    public async Task SendPlayerMove(string playerId, object movement)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        // Update stored position from movement data
        if (movement is System.Text.Json.JsonElement json && json.TryGetProperty("position", out var pos))
        {
            var newX = pos.GetProperty("x").GetDouble();
            var newY = pos.GetProperty("y").GetDouble();
            ConnectedPlayers[Context.ConnectionId] = conn with { X = newX, Y = newY };
        }

        var groupName = $"room-{conn.RoomId}";
        await Clients.OthersInGroup(groupName).SendAsync("ReceivePlayerMove", playerId, movement);
    }

    public async Task StartGame(string mapName)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        Console.WriteLine($"[GameHub] Game started in room {conn.RoomId} with map: {mapName}");
        await Clients.Group(groupName).SendAsync("GameStarted", mapName);
    }

    public async Task SendChatMessage(string sender, string message)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.Group(groupName).SendAsync("ReceiveChatMessage", sender, message);
    }

    public async Task LeaveRoom()
    {
        if (ConnectedPlayers.TryRemove(Context.ConnectionId, out var conn))
        {
            var groupName = $"room-{conn.RoomId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            await Clients.OthersInGroup(groupName).SendAsync("PlayerLeft", Context.ConnectionId);
            Console.WriteLine($"[GameHub] Player left room {conn.RoomId}: {conn.PlayerName}");

            // Limpiar entrada del jugador en Redis
            try
            {
                await _redis.HashDeleteAsync($"room:{conn.RoomId}:players", Context.ConnectionId);
                await _redis.StringDecrementAsync($"room:{conn.RoomId}:count");
            }
            catch { /* Redis no disponible */ }

            await DecrementPlayerCount(conn.RoomId);
        }
    }

    public async Task BulletFired(string playerId, double x, double y, string direction)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.OthersInGroup(groupName).SendAsync("BulletFired", playerId, x, y, direction);
    }

    public async Task TileDestroyed(int tileX, int tileY)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.OthersInGroup(groupName).SendAsync("TileDestroyed", tileX, tileY);
    }

    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"[GameHub] Client connected: {Context.ConnectionId}");
        await Clients.Caller.SendAsync("ConnectionEstablished", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectedPlayers.TryRemove(Context.ConnectionId, out var conn))
        {
            var groupName = $"room-{conn.RoomId}";
            await Clients.OthersInGroup(groupName).SendAsync("PlayerLeft", Context.ConnectionId);
            Console.WriteLine($"[GameHub] Client disconnected from room {conn.RoomId}: {conn.PlayerName}");

            await DecrementPlayerCount(conn.RoomId);
        }
        else
        {
            Console.WriteLine($"[GameHub] Client disconnected: {Context.ConnectionId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task DecrementPlayerCount(string roomId)
    {
        if (!int.TryParse(roomId, out var sessionId)) return;

        var session = await _context.GameSessions.FindAsync(sessionId);
        if (session == null) return;

        session.CurrentPlayers = Math.Max(0, session.CurrentPlayers - 1);

        if (session.CurrentPlayers <= 0)
        {
            _context.GameSessions.Remove(session);
            Console.WriteLine($"[GameHub] Room {roomId} empty, removed");
        }

        await _context.SaveChangesAsync();
    }
}
