using Shared.Models;
using System.Text.Json.Serialization;

namespace Client.Models;

public class GameInfo
{
    public Guid Id { get; set; }
    public string CreatorName { get; set; } = string.Empty;
    public int PlayersCount { get; set; }
    public int MaxPlayers { get; set; }
    public GameState State { get; set; }  // Изменено с string на GameState
    public DateTime CreatedAt { get; set; }
    public TimeSpan TimeSinceCreation => DateTime.UtcNow - CreatedAt;

    [JsonIgnore]
    public string StateDescription => GetStateDescription(State);

    private static string GetStateDescription(GameState state)
    {
        return state switch
        {
            GameState.WaitingForPlayers => "⏳ Ожидание игроков",
            GameState.Initializing => "🔄 Инициализация",
            GameState.PlayerTurn => "🎮 Идет игра",
            GameState.WaitingForNope => "⏸️ Ожидание НЕТ",
            GameState.ResolvingAction => "⚡ Разрешение действия",
            GameState.GameOver => "🏁 Игра окончена",
            GameState.Paused => "⏸️ На паузе",
            _ => state.ToString()
        };
    }
}