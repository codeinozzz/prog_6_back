using System.ComponentModel.DataAnnotations;

namespace BattleTanks_Backend.DTOs;

public record CreateRoomRequest(
    [Required, StringLength(50, MinimumLength = 2)] string RoomName,
    [Required] string MapName
);

public record RoomResponse(int Id, string RoomName, int CurrentPlayers, int MaxPlayers, string Status, string MapName);
