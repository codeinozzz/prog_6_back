namespace BattleTanks_Backend.DTOs;

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Username, string Password);
public record AuthResponse(int PlayerId, string Username, string Token);
