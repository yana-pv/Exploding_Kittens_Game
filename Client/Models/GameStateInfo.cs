using Client;
using Server.Game.Enums;

namespace Client;

public class GameStateInfo
{
    public Guid SessionId { get; set; }
    public GameState State { get; set; }
    public string? CurrentPlayer { get; set; }
    public int AlivePlayers { get; set; }
    public int CardsInDeck { get; set; }

    // ДОЛЖНЫ СОВПАДАТЬ С СЕРВЕРОМ:
    public int TurnsPlayed { get; set; }
    public string? Winner { get; set; }
    public List<PlayerInfo> Players { get; set; } = new();
}