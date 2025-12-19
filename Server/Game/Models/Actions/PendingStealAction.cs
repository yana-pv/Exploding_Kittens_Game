namespace Server.Game.Models.Actions;

public class PendingStealAction
{
    public required Guid SessionId { get; set; }
    public required Player Player { get; set; }
    public required Player Target { get; set; }
    public required List<int> CardIndices { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}