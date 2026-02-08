using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.DTOs;
using BattleTanks_Backend.Models;

namespace BattleTanks_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoomController : ControllerBase
{
    private readonly BattleTanksDbContext _context;

    public RoomController(BattleTanksDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<RoomResponse>>> GetRooms()
    {
        var rooms = await _context.GameSessions
            .Where(s => s.Status == SessionStatus.Waiting)
            .Select(s => new RoomResponse(
                s.Id,
                s.RoomName,
                s.CurrentPlayers,
                s.MaxPlayers,
                s.Status.ToString()
            ))
            .ToListAsync();

        return Ok(rooms);
    }

    [HttpPost]
    public async Task<ActionResult<RoomResponse>> CreateRoom(CreateRoomRequest request)
    {
        var session = new GameSession
        {
            RoomName = request.RoomName,
            MapName = request.MapName,
            MaxPlayers = 4,
            CurrentPlayers = 0,
            Status = SessionStatus.Waiting,
            CreatedAt = DateTime.UtcNow
        };

        _context.GameSessions.Add(session);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetRooms),
            new RoomResponse(session.Id, session.RoomName, session.CurrentPlayers, session.MaxPlayers, session.Status.ToString())
        );
    }

    [HttpPut("{id}/join")]
    public async Task<ActionResult> JoinRoom(int id)
    {
        var session = await _context.GameSessions.FindAsync(id);

        if (session == null)
            return NotFound("Room not found");

        if (session.CurrentPlayers >= session.MaxPlayers)
            return BadRequest("Room is full");

        if (session.Status != SessionStatus.Waiting)
            return BadRequest("Room is not accepting players");

        session.CurrentPlayers++;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Joined successfully", currentPlayers = session.CurrentPlayers });
    }
}
