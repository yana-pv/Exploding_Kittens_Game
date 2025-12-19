using Shared.Models;

namespace Server.Game.Models.Actions;

public class PendingExplosion
{
    public Player Player { get; set; } = null!;
    public GameSession Session { get; set; } = null!;
    public Card KittenCard { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public CancellationTokenSource TimeoutToken { get; set; } = null!;
}