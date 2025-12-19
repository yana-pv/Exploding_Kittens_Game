using Server.Game.Models;
using Shared.Models;

namespace Server.Game.Models.Actions;

public class PendingFavorAction
{
    public required Player Requester { get; set; }
    public required Player Target { get; set; }
    public required Card Card { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}