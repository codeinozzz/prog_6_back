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

    // GET /api/ranking?page=1&pageSize=10
    // AsNoTracking: EF Core no rastrea los objetos devueltos, reduciendo uso de memoria
    // y acelerando consultas de solo lectura (ranking no se va a modificar en este request)
    [HttpGet]
    public async Task<IActionResult> GetRanking([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var skip = (page - 1) * pageSize;

        var players = await _context.Players
            .AsNoTracking()
            .OrderByDescending(p => p.TotalScore)
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

    // GET /api/ranking/benchmark
    // Compara tiempos de consulta: con tracking vs sin tracking (AsNoTracking)
    // Esto demuestra el impacto real de AsNoTracking en consultas de lectura pura
    [HttpGet("benchmark")]
    public async Task<IActionResult> Benchmark()
    {
        var results = new List<object>();

        // -- Sin AsNoTracking (EF rastrea los objetos en el ChangeTracker) --
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

        // Limpiar el ChangeTracker para que no afecte la siguiente medicion
        _context.ChangeTracker.Clear();

        // -- Con AsNoTracking (EF no rastrea, mas rapido para lectura pura) --
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
