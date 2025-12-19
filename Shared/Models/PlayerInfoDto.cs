namespace Shared.Models;

public class PlayerInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CardCount { get; set; }
    public bool IsAlive { get; set; }
    public int TurnOrder { get; set; }
    public bool IsCurrentPlayer { get; set; }
}