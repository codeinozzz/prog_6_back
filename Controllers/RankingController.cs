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

}
