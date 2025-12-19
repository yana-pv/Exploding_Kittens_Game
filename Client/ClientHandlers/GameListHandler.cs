using Client.Models;
using Shared.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.ClientHandlers;

[ClientCommand(Command.GameList)]
public class GameListHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);

        try
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            };

            var games = JsonSerializer.Deserialize<List<GameInfo>>(json, options);
            if (games != null && games.Count > 0)
            {
                // Сохраняем игры в клиенте для быстрого доступа
                client.UpdateAvailableGames(games);
                // Отображаем список
                client.DisplayAvailableGames(games);
            }
            else
            {
                client.AddToLog("📭 Нет доступных игр. Создайте новую!");
            }
        }
        catch (JsonException ex)
        {
            client.AddToLog($"Ошибка разбора списка игр: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}