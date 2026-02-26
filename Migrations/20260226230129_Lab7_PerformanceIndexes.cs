using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleTanks_Backend.Migrations
{
    /// <inheritdoc />
    public partial class Lab7_PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_AchievedAt",
                table: "Scores",
                columns: new[] { "PlayerId", "AchievedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_TotalScore",
                table: "Players",
                column: "TotalScore");

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_CreatedAt",
                table: "GameSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_Status",
                table: "GameSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_AchievedAt",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Players_TotalScore",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_GameSessions_CreatedAt",
                table: "GameSessions");

            migrationBuilder.DropIndex(
                name: "IX_GameSessions_Status",
                table: "GameSessions");
        }
    }
}
