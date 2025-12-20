using Shared.Models;

namespace Server.Game.Models;

public class GameSessionInfoDto
{
    public required Guid Id { get; set; }
    public required string CreatorName { get; set; }
    public int PlayersCount { get; set; }
    public int MaxPlayers { get; set; }
    public GameState State { get; set; }
    public DateTime CreatedAt { get; set; }
}