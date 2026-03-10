namespace BattleTanks_Backend.Models;

public class Score
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public int Points { get; set; }
    public DateTime AchievedAt { get; set; }
}
