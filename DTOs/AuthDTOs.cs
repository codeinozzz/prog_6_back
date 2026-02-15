using System.ComponentModel.DataAnnotations;

namespace BattleTanks_Backend.DTOs;

public record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3)] string Username,
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password
);

public record LoginRequest(
    [Required] string Username,
    [Required] string Password
);

public record AuthResponse(int PlayerId, string Username, string Token);
