using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.TakeFromDiscard)]
public class TakeFromDiscardHandler : ICommandHandler
{
    private class PendingDiscardAction
    {
        public required Guid SessionId { get; set; }
        public required Player Player { get; set; }
        public required List<int> CardIndices { get; set; } // Используем индексы
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    private static readonly ConcurrentDictionary<Guid, PendingDiscardAction> _pendingDiscardActions = new();

    public async Task Invoke(Socket sender, GameSessionManager sessionManager,
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var data = Encoding.UTF8.GetString(payload);
        var parts = data.Split(':');

        // Формат: gameId:playerId:cardIndex
        if (parts.Length < 3 || !Guid.TryParse(parts[0], out var gameId) ||
            !Guid.TryParse(parts[1], out var playerId))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var session = sessionManager.GetSession(gameId);
        if (session == null)
        {
            await sender.SendError(CommandResponse.GameNotFound);
            return;
        }

        var player = session.GetPlayerById(playerId);
        if (player == null || player.Connection != sender)
        {
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        if (!int.TryParse(parts[2], out var cardIndex))
        {
            await player.Connection.SendMessage("❌ Неверный номер карты!");
            return;
        }

        // Проверяем, есть ли ожидание выбора из сброса
        if (!_pendingDiscardActions.TryGetValue(session.Id, out var pending) ||
            pending.Player != player)
        {
            await player.Connection.SendMessage("❌ Нет активного запроса на выбор карты из сброса!");
            return;
        }

        // Проверяем индекс (ВАЖНО: проверяем текущее состояние сброса)
        if (cardIndex < 0 || cardIndex >= session.GameDeck.DiscardPile.Count)
        {
            await player.Connection.SendMessage($"❌ Неверный номер карты! В сбросе только {session.GameDeck.DiscardPile.Count} карт (0-{session.GameDeck.DiscardPile.Count - 1})");

            // Показываем текущие карты в сбросе
            if (session.GameDeck.DiscardPile.Count > 0)
            {
                var discardCards = session.GameDeck.DiscardPile
                    .Select((card, idx) => $"{idx}. {card.Name}")
                    .ToList();

                var discardInfo = string.Join("\n", discardCards);
                await player.Connection.SendMessage($"🗑️ Текущие карты в сбросе:\n{discardInfo}");
            }

            return;
        }

        // Выполняем взятие карты
        await CompleteTakeFromDiscard(session, player, pending.CardIndices, cardIndex);

        // Убираем из ожидания
        _pendingDiscardActions.TryRemove(session.Id, out _);
    }

    private async Task CompleteTakeFromDiscard(GameSession session, Player player,
        List<int> cardIndices, int takenCardIndex)
    {
        Console.WriteLine($"DEBUG CompleteTakeFromDiscard: Начинаем обработку для игрока {player.Name}");
        Console.WriteLine($"DEBUG: Индексы карт для сброса: {string.Join(", ", cardIndices)}");
        Console.WriteLine($"DEBUG: Карт в руке до: {player.Hand.Count}");

        // Сбрасываем карты комбо по индексам
        DiscardComboCards(session, player, cardIndices);

        // Берем карту из сброса
        var takenCard = session.GameDeck.TakeFromDiscard(takenCardIndex);
        Console.WriteLine($"DEBUG: Взята карта из сброса: {takenCard.Name}");

        // Добавляем взятую карту в руку
        player.AddToHand(takenCard);

        Console.WriteLine($"DEBUG: Карт в руке после: {player.Hand.Count}");

        await session.BroadcastMessage($"🎨 {player.Name} взял карту '{takenCard.Name}' из колоды сброса используя Воровство из сброса!");

        // Обновляем руки
        await player.Connection.SendPlayerHand(player);
        await session.BroadcastGameState();
    }

    // Метод для сброса карт по индексам (аналогичен методу в UseComboHandler)
    private void DiscardComboCards(GameSession session, Player player, List<int> cardIndices)
    {
        if (cardIndices == null || cardIndices.Count == 0)
            return;

        Console.WriteLine($"DEBUG DiscardComboCards (TakeFromDiscard): Начинаем для игрока {player.Name}");
        Console.WriteLine($"DEBUG: Индексы для сброса: {string.Join(",", cardIndices)}");
        Console.WriteLine($"DEBUG: Карт в руке до: {player.Hand.Count}");

        // Сортируем по убыванию, чтобы не сбивать индексы при удалении
        var sortedIndices = cardIndices
            .OrderByDescending(i => i)
            .Distinct() // Убираем дубликаты
            .ToList();

        foreach (var index in sortedIndices)
        {
            if (index >= 0 && index < player.Hand.Count)
            {
                var card = player.Hand[index];
                Console.WriteLine($"DEBUG: Сбрасываем карту #{index}: {card.Name}");

                player.Hand.RemoveAt(index);
                session.GameDeck.Discard(card);
            }
            else
            {
                Console.WriteLine($"DEBUG: Неверный индекс {index}, карт в руке: {player.Hand.Count}");
            }
        }

        Console.WriteLine($"DEBUG: Карт в руке после: {player.Hand.Count}");
    }

    private async Task HandleDiscardTimeout(GameSession session, PendingDiscardAction pending)
    {
        var player = pending.Player;

        // Проверяем, все еще ли ожидание активно
        if (!_pendingDiscardActions.TryGetValue(session.Id, out var current) ||
            current.Timestamp != pending.Timestamp)
        {
            return; // Уже обработано
        }

        if (session.GameDeck.DiscardPile.Count == 0)
        {
            await session.BroadcastMessage("🗑️ Колода сброса пуста!");

            // Сбрасываем карты комбо
            DiscardComboCards(session, player, pending.CardIndices);

            await session.BroadcastMessage($"{player.Name} использовал комбо, но сброс пуст!");
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();

            _pendingDiscardActions.TryRemove(session.Id, out _);
            return;
        }

        // Таймаут - берем первую карту
        await CompleteTakeFromDiscard(session, player, pending.CardIndices, 0);
        await session.BroadcastMessage($"(таймаут: выбрана первая карта)");

        _pendingDiscardActions.TryRemove(session.Id, out _);
    }

    // Статический метод для создания ожидания (обновленный)
    public static void CreatePendingAction(GameSession session, Player player, List<int> cardIndices)
    {
        var action = new PendingDiscardAction
        {
            SessionId = session.Id,
            Player = player,
            CardIndices = cardIndices,
            Timestamp = DateTime.UtcNow
        };

        _pendingDiscardActions[session.Id] = action;

        Console.WriteLine($"DEBUG CreatePendingAction: Создано ожидание для игрока {player.Name}");
        Console.WriteLine($"DEBUG: Индексы карт для комбо: {string.Join(", ", cardIndices)}");

        // Таймер на 30 секунд
        Task.Delay(30000).ContinueWith(async _ =>
        {
            if (_pendingDiscardActions.TryGetValue(session.Id, out var pending) &&
                pending.Timestamp == action.Timestamp)
            {
                Console.WriteLine($"DEBUG: Таймаут для TakeFromDiscard, сессия {session.Id}");
                var handler = new TakeFromDiscardHandler();
                await handler.HandleDiscardTimeout(session, pending);
            }
        });
    }
}