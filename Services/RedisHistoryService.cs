using System.Text.Json;
using StackExchange.Redis;

namespace BattleTanks_Backend.Services;

public record GameEvent(string Type, string RoomId, string Payload, long Timestamp);

public interface IRedisHistoryService
{
    Task SaveEventAsync(string roomId, string eventType, object payload);
    Task<IEnumerable<GameEvent>> GetRoomHistoryAsync(string roomId, int count = 50);
    Task<bool> IsAvailableAsync();
}

public class RedisHistoryService : IRedisHistoryService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly int _maxHistory;
    private readonly ILogger<RedisHistoryService> _logger;

    public RedisHistoryService(IConnectionMultiplexer? redis, IConfiguration config, ILogger<RedisHistoryService> logger)
    {
        _redis = redis;
        _maxHistory = config.GetValue<int>("Redis:MaxHistoryPerRoom", 50);
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (_redis == null) return false;
        try { await _redis.GetDatabase().PingAsync(); return true; }
        catch { return false; }
    }

    public async Task SaveEventAsync(string roomId, string eventType, object payload)
    {
        if (_redis == null) return;

        try
        {
            var db = _redis.GetDatabase();
            var key = $"battletanks:room:{roomId}:events";
            var entry = JsonSerializer.Serialize(new GameEvent(
                eventType,
                roomId,
                JsonSerializer.Serialize(payload),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

            // Append to list and trim to max size
            await db.ListRightPushAsync(key, entry);
            await db.ListTrimAsync(key, -_maxHistory, -1);
            await db.KeyExpireAsync(key, TimeSpan.FromHours(2)); // auto-clean after 2h
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Failed to save event: {Msg}", ex.Message);
        }
    }

    public async Task<IEnumerable<GameEvent>> GetRoomHistoryAsync(string roomId, int count = 50)
    {
        if (_redis == null) return [];

        try
        {
            var db = _redis.GetDatabase();
            var key = $"battletanks:room:{roomId}:events";
            var entries = await db.ListRangeAsync(key, -count, -1);

            return entries
                .Select(e => JsonSerializer.Deserialize<GameEvent>(e.ToString()))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Failed to get history: {Msg}", ex.Message);
            return [];
        }
    }
}
