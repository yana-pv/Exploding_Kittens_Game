using Client;
using Server.Game.Enums;

namespace Client;

public class GameStateInfo
{
    public Guid SessionId { get; set; }          // ID игры
    public GameState State { get; set; }         // Текущее состояние (WaitingForPlayers, PlayerTurn и т.д.)
    public string? CurrentPlayer { get; set; }   // Имя текущего игрока
    public int AlivePlayers { get; set; }        // Сколько игроков еще в игре
    public int CardsInDeck { get; set; }         // Сколько карт осталось в колоде
    public int TurnsPlayed { get; set; }         // Сколько ходов сыграно
    public string? Winner { get; set; }          // Победитель (если есть)
    public List<PlayerInfo> Players { get; set; } = new(); // Список всех игроков
}