using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using BattleTanks_Backend.Data;

namespace BattleTanks_Backend.Services;

public class RankingCacheService
{
    private const string RankingKey = "ranking:global";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly BattleTanksDbContext _context;
    private readonly IDatabase _redis;

    public RankingCacheService(BattleTanksDbContext context, IConnectionMultiplexer redis)
    {
        _context = context;
        _redis = redis.GetDatabase();
    }

    public async Task<List<RankingEntry>> GetTopRankingAsync(int count = 10)
    {
        try
        {
            var cached = await _redis.SortedSetRangeByRankWithScoresAsync(
                RankingKey, 0, count - 1, Order.Descending);

            if (cached.Length > 0)
            {
                return cached.Select((e, i) => new RankingEntry(
                    Rank: i + 1,
                    Username: e.Element.ToString(),
                    TotalScore: (int)e.Score,
                    FromCache: true
                )).ToList();
            }
        }
        catch { }

        var players = await _context.Players
            .AsNoTracking()
            .OrderByDescending(p => p.TotalScore)
            .Take(count)
            .Select(p => new { p.Username, p.TotalScore })
            .ToListAsync();

        try
        {
            if (players.Count > 0)
            {
                var entries = players
                    .Select(p => new SortedSetEntry(p.Username, p.TotalScore))
                    .ToArray();

                await _redis.SortedSetAddAsync(RankingKey, entries);
                await _redis.KeyExpireAsync(RankingKey, CacheTtl);
            }
        }
        catch { }

        return players.Select((p, i) => new RankingEntry(
            Rank: i + 1,
            Username: p.Username,
            TotalScore: p.TotalScore,
            FromCache: false
        )).ToList();
    }

    public async Task UpdatePlayerScoreAsync(string username, int newTotalScore)
    {
        try { await _redis.SortedSetAddAsync(RankingKey, username, newTotalScore); }
        catch { }
    }

    public async Task InvalidateCacheAsync()
    {
        try { await _redis.KeyDeleteAsync(RankingKey); }
        catch { }
    }
}

public record RankingEntry(int Rank, string Username, int TotalScore, bool FromCache);
