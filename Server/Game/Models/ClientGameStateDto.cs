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
    // Убираем TurnsPlayed, WinnerName, NeedsToDraw, GameLog, Players, CardCounter и т.д.
    // Оставляем только самую необходимую информацию.
}