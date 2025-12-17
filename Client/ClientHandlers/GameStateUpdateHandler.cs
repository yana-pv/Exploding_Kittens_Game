using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.GameStateUpdate)]
public class GameStateUpdateHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);

        try
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());

            var state = JsonSerializer.Deserialize<GameStateInfo>(json, options);
            if (state != null)
            {
                // 1. Обновляем текущее состояние игры
                client.CurrentGameState = state.State;

                // 2. Обновляем список игроков (если сервер отправляет)
                if (state.Players != null && state.Players.Count > 0)
                {
                    client.OtherPlayers.Clear();
                    client.OtherPlayers.AddRange(state.Players);
                }

                // 3. Обрабатываем текущего игрока
                if (!string.IsNullOrEmpty(state.CurrentPlayer))
                {
                    var isMyTurn = state.CurrentPlayer == client.PlayerName;
                    client.AddToLog($"Сейчас ходит: {state.CurrentPlayer}");

                    if (isMyTurn)
                    {
                        client.AddToLog("🎮 ВАШ ХОД! Вы можете сыграть карту или взять карту из колоды.");
                    }
                }

                // 4. Обрабатываем победителя (если сервер отправляет)
                if (!string.IsNullOrEmpty(state.Winner))
                {
                    if (state.Winner == client.PlayerName)
                    {
                        client.AddToLog("🎉 ПОБЕДА! Вы выиграли игру!");
                        client.SessionId = null; // Сбрасываем сессию при победе
                    }
                    else
                    {
                        client.AddToLog($"🏆 Победитель: {state.Winner}");
                    }
                }

                // 5. Выводим дополнительную информацию
                client.AddToLog($"Игроков в игре: {state.AlivePlayers}");
                client.AddToLog($"Карт в колоде: {state.CardsInDeck}");

                // 6. Выводим информацию о ходе (если сервер отправляет)
                if (state.TurnsPlayed > 0)
                {
                    client.AddToLog($"Ходов сыграно: {state.TurnsPlayed}");
                }
            }
        }
        catch (JsonException ex)
        {
            client.AddToLog($"Ошибка разбора состояния игры: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
