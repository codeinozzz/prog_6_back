namespace BattleTanks_Backend.Models;

public class Score
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int Points { get; set; }
    public DateTime AchievedAt { get; set; }
}
