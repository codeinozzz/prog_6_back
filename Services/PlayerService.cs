using System.Diagnostics;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace BattleTanks_Backend.Services;

public class PlayerService
{
    private readonly BattleTanksDbContext _context;
    private readonly ILogger<PlayerService> _logger;

    public PlayerService(BattleTanksDbContext context, ILogger<PlayerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Player?> FindByUsernameAsync(string username)
    {
        var sw = Stopwatch.StartNew();
        var result = await _context.Players.FirstOrDefaultAsync(p => p.Username == username);
        sw.Stop();
        _logger.LogInformation("[Profiling] PlayerService.FindByUsername username={Username} elapsed={Ms}ms", username, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task DecrementSessionAsync(int sessionId)
    {
        var sw = Stopwatch.StartNew();

        var session = await _context.GameSessions.FindAsync(sessionId);
        if (session == null) return;

        session.CurrentPlayers = Math.Max(0, session.CurrentPlayers - 1);

        if (session.CurrentPlayers <= 0)
        {
            _context.GameSessions.Remove(session);
            Console.WriteLine($"[GameHub] Room {sessionId} empty, removed");
        }

        await _context.SaveChangesAsync();
        sw.Stop();
        _logger.LogInformation("[Profiling] PlayerService.DecrementSession sessionId={Id} elapsed={Ms}ms", sessionId, sw.ElapsedMilliseconds);
    }

    public async Task SaveGameResultAsync(string username, int points, bool isVictory)
    {
        var player = await _context.Players.FirstOrDefaultAsync(p => p.Username == username);
        if (player == null) return;

        _context.Scores.Add(new Score
        {
            PlayerId = player.Id,
            Points = points,
            AchievedAt = DateTime.UtcNow
        });

        player.TotalScore += points;
        player.GamesPlayed++;
        if (isVictory) player.Victories++;

        await _context.SaveChangesAsync();
        _logger.LogInformation("[Score] Saved result for {Username}: points={Points} victory={Victory}", username, points, isVictory);
    }
}
