namespace BattleTanks_Backend.Models;

public class Player
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int GamesPlayed { get; set; }
    public int Victories { get; set; }
    public int TotalScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<GameSession> GameSessions { get; set; } = new();
}
