namespace Client;

public class PlayerInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CardCount { get; set; }        // Сколько карт у игрока (не показываем какие именно!)
    public bool IsAlive { get; set; }         // Жив или выбыл
    public int TurnOrder { get; set; }        // Порядок хода
    public bool IsCurrentPlayer { get; set; } // Сейчас его ход?
}