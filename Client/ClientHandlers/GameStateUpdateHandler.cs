using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // Добавлено
using Server.Networking.Commands;

namespace Client;

[ClientCommand(Command.GameStateUpdate)]
public class GameStateUpdateHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);

        try
        {
            // --- Добавлено: Настройка опций десериализации ---
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter()); // <-- Конвертер enum-строка
            // --- Конец изменений ---

            var state = JsonSerializer.Deserialize<GameStateInfo>(json, options); // <-- Передаём опции
            if (state != null)
            {
                client.CurrentGameState = state.State;

                if (!string.IsNullOrEmpty(state.CurrentPlayer))
                {
                    var isMyTurn = state.CurrentPlayer == client.PlayerName;
                    client.AddToLog($"Сейчас ходит: {state.CurrentPlayer}");

                    if (isMyTurn)
                    {
                        client.AddToLog("🎮 ВАШ ХОД! Вы можете сыграть карту или взять карту из колоды.");
                    }
                }

                if (state.Winner != null)
                {
                    if (state.Winner == client.PlayerName)
                    {
                        client.AddToLog("🎉 ПОБЕДА! Вы выиграли игру!");
                    }
                    else
                    {
                        client.AddToLog($"🏆 Победитель: {state.Winner}");
                    }
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