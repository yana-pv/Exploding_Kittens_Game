namespace Server.Game.Models.Actions;

public class PendingDiscardAction
{
    public required Guid SessionId { get; set; }
    public required Player Player { get; set; }
    public required List<int> CardIndices { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}