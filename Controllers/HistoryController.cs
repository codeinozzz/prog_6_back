using Microsoft.AspNetCore.Mvc;
using BattleTanks_Backend.Services;

namespace BattleTanks_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly IRedisHistoryService _history;

    public HistoryController(IRedisHistoryService history)
    {
        _history = history;
    }

    [HttpGet("{roomId}")]
    public async Task<IActionResult> GetRoomHistory(string roomId, [FromQuery] int count = 50)
    {
        var events = await _history.GetRoomHistoryAsync(roomId, count);
        return Ok(events);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var available = await _history.IsAvailableAsync();
        return Ok(new { redis = available ? "connected" : "unavailable" });
    }
}
