// File: Server/Game/Models/ClientGameStateDto.cs
using Server.Game.Enums;

namespace Server.Game.Models;

public class ClientGameStateDto
{
    public Guid SessionId { get; set; }
    public GameState State { get; set; }
    public string? CurrentPlayerName { get; set; }
    public int AlivePlayers { get; set; }
    public int CardsInDeck { get; set; }

    // ДОБАВЬТЕ ЭТИ СВОЙСТВА:
    public int TurnsPlayed { get; set; }
    public string? WinnerName { get; set; }
    public List<PlayerInfoDto> Players { get; set; } = new();
}