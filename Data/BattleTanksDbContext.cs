using Microsoft.EntityFrameworkCore;
using BattleTanks_Backend.Models;

namespace BattleTanks_Backend.Data;

public class BattleTanksDbContext : DbContext
{
    public BattleTanksDbContext(DbContextOptions<BattleTanksDbContext> options)
        : base(options) { }

    public DbSet<Player> Players { get; set; }
    public DbSet<GameSession> GameSessions { get; set; }
    public DbSet<Score> Scores { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>()
            .HasIndex(p => p.Username)
            .IsUnique();

        modelBuilder.Entity<Player>()
            .HasIndex(p => p.Email)
            .IsUnique();

        modelBuilder.Entity<Player>()
            .HasIndex(p => p.TotalScore)
            .HasDatabaseName("IX_Players_TotalScore");

        modelBuilder.Entity<GameSession>()
            .HasIndex(gs => gs.Status)
            .HasDatabaseName("IX_GameSessions_Status");

        modelBuilder.Entity<GameSession>()
            .HasIndex(gs => gs.CreatedAt)
            .HasDatabaseName("IX_GameSessions_CreatedAt");

        modelBuilder.Entity<Score>()
            .HasIndex(s => s.PlayerId)
            .HasDatabaseName("IX_Scores_PlayerId");

        modelBuilder.Entity<Score>()
            .HasIndex(s => new { s.PlayerId, s.AchievedAt })
            .HasDatabaseName("IX_Scores_PlayerId_AchievedAt");

        modelBuilder.Entity<Score>()
            .HasOne(s => s.Player)
            .WithMany()
            .HasForeignKey(s => s.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
