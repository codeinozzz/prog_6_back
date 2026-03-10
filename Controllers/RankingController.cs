using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BattleTanks_Backend.Data;

namespace BattleTanks_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RankingController : ControllerBase
{
    private readonly BattleTanksDbContext _context;

    public RankingController(BattleTanksDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetRanking([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string sortBy = "victories")
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var skip = (page - 1) * pageSize;

        var query = _context.Players.AsNoTracking();

        var ordered = sortBy == "score"
            ? query.OrderByDescending(p => p.TotalScore)
            : query.OrderByDescending(p => p.Victories).ThenByDescending(p => p.TotalScore);

        var players = await ordered
            .Skip(skip)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Username,
                p.TotalScore,
                p.Victories,
                p.GamesPlayed
            })
            .ToListAsync();

        var totalPlayers = await _context.Players.AsNoTracking().CountAsync();

        return Ok(new
        {
            page,
            pageSize,
            totalPlayers,
            totalPages = (int)Math.Ceiling(totalPlayers / (double)pageSize),
            data = players
        });
    }

    [HttpGet("benchmark")]
    public async Task<IActionResult> Benchmark()
    {
        var results = new List<object>();

        var swTracked = Stopwatch.StartNew();
        var tracked = await _context.Players
            .OrderByDescending(p => p.TotalScore)
            .Take(10)
            .ToListAsync();
        swTracked.Stop();

        results.Add(new
        {
            mode = "WithTracking",
            records = tracked.Count,
            elapsedMs = swTracked.Elapsed.TotalMilliseconds
        });

        _context.ChangeTracker.Clear();

        var swNoTracking = Stopwatch.StartNew();
        var noTracking = await _context.Players
            .AsNoTracking()
            .OrderByDescending(p => p.TotalScore)
            .Take(10)
            .ToListAsync();
        swNoTracking.Stop();

        results.Add(new
        {
            mode = "AsNoTracking",
            records = noTracking.Count,
            elapsedMs = swNoTracking.Elapsed.TotalMilliseconds
        });

        return Ok(new
        {
            description = "Comparativa de rendimiento: tracking vs AsNoTracking",
            results
        });
    }
}
