using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using BattleTanks_Backend.Data;

namespace BattleTanks_Backend.Services;

// Servicio de cache de ranking usando Redis SortedSet
// Estructura en Redis:
//   ranking:global -> SortedSet donde score=TotalScore y member=Username
//   TTL: 5 minutos (se invalida al terminar cada partida)
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

    // Devuelve el top N de jugadores. Primero intenta Redis, si no hay cache consulta PostgreSQL
    public async Task<List<RankingEntry>> GetTopRankingAsync(int count = 10)
    {
        // Intentar leer desde Redis SortedSet (descendente por puntos)
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
        catch
        {
            // Redis no disponible: continuar con PostgreSQL
        }

        // Cache miss o Redis no disponible: consulta PostgreSQL con AsNoTracking
        var players = await _context.Players
            .AsNoTracking()
            .OrderByDescending(p => p.TotalScore)
            .Take(count)
            .Select(p => new { p.Username, p.TotalScore })
            .ToListAsync();

        // Actualizar Redis con los datos de PostgreSQL
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
        catch
        {
            // Redis no disponible, continuar sin cache
        }

        return players.Select((p, i) => new RankingEntry(
            Rank: i + 1,
            Username: p.Username,
            TotalScore: p.TotalScore,
            FromCache: false
        )).ToList();
    }

    // Actualiza el score de un jugador en Redis cuando termina una partida
    public async Task UpdatePlayerScoreAsync(string username, int newTotalScore)
    {
        try { await _redis.SortedSetAddAsync(RankingKey, username, newTotalScore); }
        catch { /* Redis no disponible */ }
    }

    // Invalida el cache completo (util tras una partida que cambia muchos scores)
    public async Task InvalidateCacheAsync()
    {
        try { await _redis.KeyDeleteAsync(RankingKey); }
        catch { /* Redis no disponible */ }
    }
}

public record RankingEntry(int Rank, string Username, int TotalScore, bool FromCache);
