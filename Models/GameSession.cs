namespace BattleTanks_Backend.Models;

public class GameSession
{
    public int Id { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public int MaxPlayers { get; set; } = 4;
    public int CurrentPlayers { get; set; }
    public string MapName { get; set; } = string.Empty;
    public SessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Player> Players { get; set; } = new();
}

public enum SessionStatus
{
    Waiting,
    InProgress,
    Finished
}
