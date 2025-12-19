using Shared.Models;

namespace Server.Game.Models;

public class ClientGameStateDto
{
    public Guid SessionId { get; set; }
    public GameState State { get; set; }
    public string? CurrentPlayerName { get; set; }
    public int AlivePlayers { get; set; }
    public int CardsInDeck { get; set; }
    public int TurnsPlayed { get; set; }
    public string? WinnerName { get; set; }
    public List<PlayerInfoDto> Players { get; set; } = new();
}