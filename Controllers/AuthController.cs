using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BattleTanks_Backend.Data;
using BattleTanks_Backend.DTOs;
using BattleTanks_Backend.Models;

namespace BattleTanks_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly BattleTanksDbContext _context;
    private readonly IConfiguration _config;

    public AuthController(BattleTanksDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

        var token = GenerateJwtToken(player);
        return Ok(new AuthResponse(player.Id, player.Username, token));
    
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var player = await _context.Players
            .FirstOrDefaultAsync(p => p.Username == request.Username);

        if (player == null || !BCrypt.Net.BCrypt.Verify(request.Password, player.PasswordHash))
            return Unauthorized("Invalid credentials");

        var token = GenerateJwtToken(player);
        return Ok(new AuthResponse(player.Id, player.Username, token));
    }

    private string GenerateJwtToken(Player player)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
            new Claim(ClaimTypes.Name, player.Username),
            new Claim(ClaimTypes.Email, player.Email)
        };

        var expireMinutes = int.Parse(_config["Jwt:ExpireMinutes"] ?? "120");

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expireMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
