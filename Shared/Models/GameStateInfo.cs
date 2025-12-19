using Shared.Models;

namespace Client;

public class GameStateInfo
{
    public Guid SessionId { get; set; }
    public GameState State { get; set; }
    public string? CurrentPlayer { get; set; }
    public int AlivePlayers { get; set; }
    public int CardsInDeck { get; set; }
    public int TurnsPlayed { get; set; }
    public string? Winner { get; set; }
    public List<PlayerInfoDto> Players { get; set; } = new();
}