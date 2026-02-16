using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.Models;
using BattleTanks_Backend.Services;

namespace BattleTanks_Backend.Hubs;

public record PlayerConnection(string PlayerName, string RoomId, double X = 0, double Y = 0);

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, PlayerConnection> ConnectedPlayers = new();
    private static readonly Random _random = new();

    private readonly BattleTanksDbContext _context;
    private readonly IMqttGameService _mqtt;
    private readonly IRedisHistoryService _history;

    public GameHub(BattleTanksDbContext context, IMqttGameService mqtt, IRedisHistoryService history)
    {
        _context = context;
        _mqtt = mqtt;
        _history = history;
    }

    public async Task JoinGame(string playerId, string playerName, string roomId, double x = 0, double y = 0)
    {
        ConnectedPlayers[Context.ConnectionId] = new PlayerConnection(playerName, roomId, x, y);

        var groupName = $"room-{roomId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        var existingPlayers = ConnectedPlayers
            .Where(kvp => kvp.Key != Context.ConnectionId && kvp.Value.RoomId == roomId)
            .Select(kvp => new { playerId = kvp.Key, playerName = kvp.Value.PlayerName, x = kvp.Value.X, y = kvp.Value.Y })
            .ToList();

        await Clients.Caller.SendAsync("ExistingPlayers", existingPlayers);

        Console.WriteLine($"[GameHub] Player joined room {roomId}: {playerName} ({playerId})");
        await Clients.OthersInGroup(groupName).SendAsync("PlayerJoined", playerId, playerName, x, y);

        // Enviar historial de Redis al jugador que se une
        var history = await _history.GetRoomHistoryAsync(roomId, 20);
        if (history.Any())
            await Clients.Caller.SendAsync("RoomHistory", history);
    }

    public async Task SendPlayerMove(string playerId, object movement)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

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

        // Generar power-ups iniciales via MQTT
        await SpawnInitialPowerUps(conn.RoomId);
    }

    public async Task SendChatMessage(string sender, string message)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.Group(groupName).SendAsync("ReceiveChatMessage", sender, message);

        // También publicar en MQTT y guardar en Redis
        await _mqtt.PublishChatAsync(conn.RoomId, sender, message);
        await _history.SaveEventAsync(conn.RoomId, "chat", new { sender, message });
    }

    public async Task LeaveRoom()
    {
        if (ConnectedPlayers.TryRemove(Context.ConnectionId, out var conn))
        {
            var groupName = $"room-{conn.RoomId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            await Clients.OthersInGroup(groupName).SendAsync("PlayerLeft", Context.ConnectionId);
            Console.WriteLine($"[GameHub] Player left room {conn.RoomId}: {conn.PlayerName}");

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

        // 30% de probabilidad de spawn power-up tras destruir tile
        if (_random.NextDouble() < 0.30)
            await SpawnPowerUp(conn.RoomId, tileX * 40, tileY * 40);
    }

    /// <summary>Jugador reporta colisión (bala impacta tanque enemigo)</summary>
    public async Task ReportCollision(string victimId, int damage)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var collision = new CollisionEvent(
            AttackerId: Context.ConnectionId,
            VictimId: victimId,
            RoomId: conn.RoomId,
            Damage: damage,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await _mqtt.PublishCollisionAsync(conn.RoomId, collision);
        await _history.SaveEventAsync(conn.RoomId, "collision", collision);
    }

    /// <summary>Jugador recolecta un power-up</summary>
    public async Task CollectPowerUp(string powerUpId)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        await _mqtt.PublishPowerUpCollectedAsync(conn.RoomId, powerUpId, Context.ConnectionId);
        await _history.SaveEventAsync(conn.RoomId, "powerup_collected",
            new { powerUpId, collectorId = Context.ConnectionId, collectorName = conn.PlayerName });
    }

    /// <summary>Notificar fin de partida</summary>
    public async Task EndGame(string winnerId, string winnerName)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.Group(groupName).SendAsync("GameOver", winnerId, winnerName);

        await _mqtt.PublishGameEndAsync(conn.RoomId, winnerId, winnerName);
        await _history.SaveEventAsync(conn.RoomId, "game_end", new { winnerId, winnerName });
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

    // ---- Private helpers ----

    private async Task SpawnInitialPowerUps(string roomId)
    {
        var types = new[] { "ammo", "health", "speed" };
        var spawnPositions = new (double x, double y)[] { (80, 80), (280, 80), (160, 200) };

        for (int i = 0; i < spawnPositions.Length; i++)
        {
            await SpawnPowerUp(roomId, spawnPositions[i].x, spawnPositions[i].y,
                types[i % types.Length], $"pu-initial-{i}");
        }
    }

    private async Task SpawnPowerUp(string roomId, double x, double y,
        string? type = null, string? id = null)
    {
        var types = new[] { "ammo", "health", "speed" };
        var powerUp = new PowerUpEvent(
            Id: id ?? $"pu-{Guid.NewGuid():N}",
            Type: type ?? types[_random.Next(types.Length)],
            X: x,
            Y: y,
            RoomId: roomId,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await _mqtt.PublishPowerUpSpawnedAsync(roomId, powerUp);
        await _history.SaveEventAsync(roomId, "powerup_spawned", powerUp);
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
