using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.DTOs;
using BattleTanks_Backend.Models;

namespace BattleTanks_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly BattleTanksDbContext _context;

    public AuthController(BattleTanksDbContext context)
    {
        _context = context;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (await _context.Players.AnyAsync(p => p.Username == request.Username))
            return BadRequest("Username already exists");

        if (await _context.Players.AnyAsync(p => p.Email == request.Email))
            return BadRequest("Email already exists");

        var player = new Player
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _context.Players.Add(player);
        await _context.SaveChangesAsync();

        return Ok(new AuthResponse(player.Id, player.Username, "simple-token-" + player.Id));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var player = await _context.Players
            .FirstOrDefaultAsync(p => p.Username == request.Username);

        if (player == null || !BCrypt.Net.BCrypt.Verify(request.Password, player.PasswordHash))
            return Unauthorized("Invalid credentials");

        return Ok(new AuthResponse(player.Id, player.Username, "simple-token-" + player.Id));
    }
}
