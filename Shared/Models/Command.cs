namespace Shared.Models;

public enum Command : byte
{
    // Управление игрой
    CreateGame = 0x10,
    JoinGame = 0x11,
    LeaveGame = 0x12,
    StartGame = 0x13,
    EndTurn = 0x14,          

    // Игровые действия
    PlayCard = 0x20,
    DrawCard = 0x21,
    UseCombo = 0x22,
    TargetPlayer = 0x23,     
    PlayNope = 0x24,
    PlayDefuse = 0x25,
    PlayFavor = 0x26,
    StealCard = 0x27,        
    TakeFromDiscard = 0x28,  


    // Информационные
    GetGameState = 0x30,
    GetPlayerHand = 0x31,
    GetPlayers = 0x32,
    GetAvailableGames = 0x33,
    GameList = 0x34,         


    // Ответы сервера
    GameCreated = 0x40,
    PlayerJoined = 0x41,
    GameStarted = 0x42,
    GameStateUpdate = 0x43,
    PlayerHandUpdate = 0x44,
    CardPlayed = 0x45,
    CardDrawn = 0x46,
    PlayerEliminated = 0x47,
    GameOver = 0x48,
    Error = 0x49,
    Message = 0x4A,
    NeedToDraw = 0x4B       
}
