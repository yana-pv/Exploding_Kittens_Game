namespace Server.Game.Enums;

public enum GameState
{
    WaitingForPlayers,    // Ожидание игроков (2-5)
    Initializing,         // Раздача карт
    PlayerTurn,           // Ход текущего игрока
    WaitingForNope,       // Ожидание карт Nope
    ResolvingAction,      // Разрешение действия карты
    GameOver,             // Игра окончена
    Paused                // Игра на паузе
}