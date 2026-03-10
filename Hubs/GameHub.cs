using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.Models;
using BattleTanks_Backend.Services;

namespace BattleTanks_Backend.Hubs;

public record PlayerConnection(string PlayerName, string RoomId, double X = 0, double Y = 0);

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, PlayerConnection> ConnectedPlayers = new();
    private static readonly Random _random = new();

    private readonly PlayerService _playerService;
    private readonly IMqttGameService _mqtt;
    private readonly IRedisHistoryService _history;
    private readonly IDatabase _redis;
    private readonly BattleTanksDbContext _context;

    public GameHub(PlayerService playerService, IMqttGameService mqtt, IRedisHistoryService history, IConnectionMultiplexer redis, BattleTanksDbContext context)
    {
        _playerService = playerService;
        _mqtt = mqtt;
        _history = history;
        _redis = redis.GetDatabase();
        _context = context;
    }

    public async Task JoinGame(string playerId, string playerName, string roomId, double x = 0, double y = 0)
    {
        ConnectedPlayers[Context.ConnectionId] = new PlayerConnection(playerName, roomId, x, y);

        if (string.IsNullOrEmpty(roomId))
        {
            Console.WriteLine($"[GameHub] Player registered (no room yet): {playerName}");
            return;
        }

        await JoinRoomGroup(roomId);
    }

    private async Task JoinRoomGroup(string roomId)
    {
        var conn = ConnectedPlayers[Context.ConnectionId];
        var groupName = $"room-{roomId}";

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        var existingPlayers = ConnectedPlayers
            .Where(kvp => kvp.Key != Context.ConnectionId && kvp.Value.RoomId == roomId)
            .Select(kvp => new { playerId = kvp.Key, playerName = kvp.Value.PlayerName, x = kvp.Value.X, y = kvp.Value.Y })
            .ToList();

        await Clients.Caller.SendAsync("ExistingPlayers", existingPlayers);
        await Clients.OthersInGroup(groupName).SendAsync("PlayerJoined", Context.ConnectionId, conn.PlayerName, conn.X, conn.Y);

        Console.WriteLine($"[GameHub] Player joined room {roomId}: {conn.PlayerName}");

        var history = await _history.GetRoomHistoryAsync(roomId, 20);
        if (history.Any())
            await Clients.Caller.SendAsync("RoomHistory", history);

        try
        {
            await _redis.HashSetAsync($"room:{roomId}:players", Context.ConnectionId, conn.PlayerName);
            await _redis.StringIncrementAsync($"room:{roomId}:count");
        }
        catch { }
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

        if (int.TryParse(conn.RoomId, out var sessionId))
        {
            var session = await _context.GameSessions.FindAsync(sessionId);
            if (session != null)
            {
                session.Status = SessionStatus.InProgress;
                await _context.SaveChangesAsync();
            }
        }

        var initialPowerUps = BuildInitialPowerUps(conn.RoomId);
        await Clients.Group(groupName).SendAsync("InitialPowerUps", initialPowerUps);

        foreach (var pu in initialPowerUps)
            await _history.SaveEventAsync(conn.RoomId, "powerup_spawned", pu);
    }

    private static List<PowerUpEvent> BuildInitialPowerUps(string roomId)
    {
        var types = new[] { "ammo", "health", "speed" };
        var positions = new (double x, double y)[] { (80, 80), (280, 80), (160, 200) };
        var result = new List<PowerUpEvent>();
        for (int i = 0; i < positions.Length; i++)
        {
            result.Add(new PowerUpEvent(
                Id: $"pu-initial-{i}",
                Type: types[i],
                X: positions[i].x,
                Y: positions[i].y,
                RoomId: roomId,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        return result;
    }

    public async Task SendChatMessage(string sender, string message)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.Group(groupName).SendAsync("ReceiveChatMessage", sender, message);

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

            try
            {
                await _redis.HashDeleteAsync($"room:{conn.RoomId}:players", Context.ConnectionId);
                await _redis.StringDecrementAsync($"room:{conn.RoomId}:count");
            }
            catch { }
        }
    }

    public async Task SetRoom(string roomId)
    {
        if (!ConnectedPlayers.TryGetValue(Context.ConnectionId, out var conn)) return;

        var oldGroup = string.IsNullOrEmpty(conn.RoomId) ? null : $"room-{conn.RoomId}";
        var newGroup = $"room-{roomId}";

        if (oldGroup != null && oldGroup != newGroup)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldGroup);

        ConnectedPlayers[Context.ConnectionId] = conn with { RoomId = roomId };

        await JoinRoomGroup(roomId);

        if (int.TryParse(roomId, out var sessionId))
        {
            var session = await _context.GameSessions.FindAsync(sessionId);
            if (session?.Status == SessionStatus.InProgress)
            {
                Console.WriteLine($"[GameHub] Late join: sending GameStarted to {conn.PlayerName} in room {roomId}");
                await Clients.Caller.SendAsync("GameStarted", session.MapName);
                var powerUps = BuildInitialPowerUps(roomId);
                await Clients.Caller.SendAsync("InitialPowerUps", powerUps);
            }
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

        if (_random.NextDouble() < 0.30)
            await SpawnPowerUp(conn.RoomId, tileX * 40, tileY * 40);
    }

    public async Task PlayerDied(string victimId, string victimName, string killerId, string killerName)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.Group(groupName).SendAsync("PlayerDied", victimId, victimName, killerId, killerName);
        await _history.SaveEventAsync(conn.RoomId, "player_died", new { victimId, victimName, killerId, killerName });
        Console.WriteLine($"[GameHub] {victimName} was killed by {killerName} in room {conn.RoomId}");
    }

    public async Task SubmitScore(string playerName, int points)
    {
        await _playerService.SaveGameResultAsync(playerName, points, isVictory: false);
        Console.WriteLine($"[GameHub] Score submitted for {playerName}: {points} pts");
    }

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

        await Clients.Client(victimId).SendAsync("PlayerHit", Context.ConnectionId, damage);

        await _mqtt.PublishCollisionAsync(conn.RoomId, collision);
        await _history.SaveEventAsync(conn.RoomId, "collision", collision);
    }

    public async Task CollectPowerUp(string powerUpId)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        await _mqtt.PublishPowerUpCollectedAsync(conn.RoomId, powerUpId, Context.ConnectionId);
        await _history.SaveEventAsync(conn.RoomId, "powerup_collected",
            new { powerUpId, collectorId = Context.ConnectionId, collectorName = conn.PlayerName });
    }

    public async Task EndGame(string winnerId, string winnerName, int finalScore = 0)
    {
        var conn = ConnectedPlayers.GetValueOrDefault(Context.ConnectionId);
        if (conn == null) return;

        var groupName = $"room-{conn.RoomId}";
        await Clients.Group(groupName).SendAsync("GameOver", winnerId, winnerName);

        await _mqtt.PublishGameEndAsync(conn.RoomId, winnerId, winnerName);
        await _history.SaveEventAsync(conn.RoomId, "game_end", new { winnerId, winnerName, finalScore });

        if (int.TryParse(conn.RoomId, out var sessionId))
        {
            var session = await _context.GameSessions.FindAsync(sessionId);
            if (session != null)
            {
                session.Status = SessionStatus.Finished;
                await _context.SaveChangesAsync();
            }
        }

        await _playerService.SaveGameResultAsync(winnerName, finalScore, isVictory: true);
        Console.WriteLine($"[GameHub] Game over in room {conn.RoomId}. Winner: {winnerName} ({finalScore} pts)");
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
        await _playerService.DecrementSessionAsync(sessionId);
    }
}
