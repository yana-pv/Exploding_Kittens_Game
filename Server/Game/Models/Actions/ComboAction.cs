namespace Server.Game.Models.Actions;

public class ComboAction
{
    public Guid SessionId { get; set; }
    public Guid PlayerId { get; set; }
    public int ComboType { get; set; }
    public List<int> CardIndices { get; set; } = new();
    public string? TargetData { get; set; }
    public DateTime Timestamp { get; set; }
}