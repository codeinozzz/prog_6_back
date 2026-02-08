namespace BattleTanks_Backend.DTOs;

public record CreateRoomRequest(string RoomName, string MapName);
public record RoomResponse(int Id, string RoomName, int CurrentPlayers, int MaxPlayers, string Status);
