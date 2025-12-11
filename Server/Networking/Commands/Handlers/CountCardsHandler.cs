using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.GetGameState)] // Используем существующую команду
public class CountCardsHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, // <-- Изменено
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (!Guid.TryParse(Encoding.UTF8.GetString(payload), out var gameId))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // Получаем сессию напрямую из менеджера
        var session = sessionManager.GetSession(gameId); // <-- Изменено
        if (session == null) // <-- Изменено условие
        {
            await sender.SendError(CommandResponse.GameNotFound);
            return;
        }

        try
        {
            var counts = new Dictionary<string, int>();

            // Подсчитываем карты по типам
            foreach (CardType type in Enum.GetValues(typeof(CardType)))
            {
                var total = session.CardCounter.GetTotalRemaining(type);
                if (total > 0)
                {
                    var card = Card.Create(type);
                    counts[card.Name] = total;
                }
            }

            var result = new StringBuilder();
            result.AppendLine("Оставшиеся карты в игре:");

            foreach (var kvp in counts.OrderBy(k => k.Key))
            {
                result.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            result.AppendLine($"Всего карт в колоде: {session.GameDeck.CardsRemaining}");
            result.AppendLine($"Карт в сбросе: {session.GameDeck.DiscardPile.Count}");

            await sender.SendMessage(result.ToString());
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при подсчете карт: {ex.Message}");
        }
    }
}