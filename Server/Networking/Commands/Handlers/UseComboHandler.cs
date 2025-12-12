using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.UseCombo)]
public class UseComboHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, PendingStealAction> _pendingSteals = new();

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

        if (parts.Length < 4 || !Guid.TryParse(parts[0], out var gameId) ||
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

        if (session.State != GameState.PlayerTurn || session.CurrentPlayer != player)
        {
            await sender.SendError(CommandResponse.NotYourTurn);
            return;
        }

        if (!int.TryParse(parts[2], out var comboType))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // ИСПРАВЛЕНИЕ: парсим ИНДЕКСЫ карт вместо типов
        var cardIndices = parts[3].Split(',')
            .Where(s => int.TryParse(s, out _))
            .Select(s => int.Parse(s))
            .ToList();

        if (cardIndices.Count != comboType)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // Проверяем, что индексы валидны
        if (cardIndices.Any(i => i < 0 || i >= player.Hand.Count))
        {
            await sender.SendError(CommandResponse.CardNotFound);
            return;
        }

        try
        {
            // Проверяем, что карты подходят для комбо
            if (!ValidateCombo(player, comboType, cardIndices))
            {
                await sender.SendError(CommandResponse.InvalidAction);
                return;
            }

            // Обрабатываем комбо в зависимости от типа
            switch (comboType)
            {
                case 2: // Две одинаковые - слепой карманник
                    await HandleTwoOfAKind(session, player, cardIndices, parts.Length > 4 ? parts[4] : null);
                    break;

                case 3: // Три одинаковые - время рыбачить
                    await HandleThreeOfAKind(session, player, cardIndices, parts.Length > 4 ? parts[4] : null);
                    break;

                case 5: // Пять разных - воруй из колоды сброса
                    await HandleFiveDifferent(session, player, cardIndices);
                    break;

                default:
                    await sender.SendError(CommandResponse.InvalidAction);
                    return;
            }
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при использовании комбо: {ex.Message}");
        }
    }

    private void DiscardComboCards(GameSession session, Player player, List<int> cardIndices)
    {
        if (cardIndices == null || cardIndices.Count == 0)
            return;

        Console.WriteLine($"DEBUG DiscardComboCards: Начинаем для игрока {player.Name}");
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

    private bool ValidateCombo(Player player, int comboType, List<int> cardIndices)
    {
        if (cardIndices.Count != comboType)
            return false;

        if (cardIndices.Any(i => i < 0 || i >= player.Hand.Count))
            return false;

        var cards = cardIndices.Select(i => player.Hand[i]).ToList();

        switch (comboType)
        {
            case 2: // Две одинаковые карты или с одинаковыми иконками
                return cards[0].Type == cards[1].Type ||
                       cards[0].IconId == cards[1].IconId;

            case 3: // Три одинаковые карты или с одинаковыми иконками
                return (cards[0].Type == cards[1].Type &&
                        cards[1].Type == cards[2].Type) ||
                       (cards[0].IconId == cards[1].IconId &&
                        cards[1].IconId == cards[2].IconId);

            case 5: // Пять разных карт с разными иконками
                return cards.Select(c => c.IconId).Distinct().Count() == 5;

            default:
                return false;
        }
    }

    private async Task HandleTwoOfAKind(GameSession session, Player player, List<int> cardIndices, string? targetPlayerId)
    {
        if (string.IsNullOrEmpty(targetPlayerId) || !Guid.TryParse(targetPlayerId, out var targetId))
        {
            await player.Connection.SendMessage("❌ Укажите ID игрока для кражи карты!");
            throw new ArgumentException("Не указан целевой игрок");
        }

        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            await player.Connection.SendMessage("❌ Некорректный целевой игрок!");
            throw new ArgumentException("Некорректный целевой игрок");
        }

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"🎭 {target.Name} не имеет карт для кражи!");
            // Сбрасываем карты комбо
            DiscardComboCards(session, player, cardIndices);
            await session.BroadcastMessage($"{player.Name} использовал Слепой Карманник, но {target.Name} не имеет карт!");
            return;
        }

        // Ждем 5 секунд на карты "Нет"
        PlayNopeHandler.StartNopeWindow(session);
        await Task.Delay(5000);

        if (PlayNopeHandler.IsActionNoped(session.Id))
        {
            await session.BroadcastMessage("⚡ Слепой Карманник отменен картой НЕТ!");
            PlayNopeHandler.CleanupNopeWindow(session.Id);
            return;
        }

        PlayNopeHandler.CleanupNopeWindow(session.Id);

        // ПОКАЗЫВАЕМ карты цели РУБАШКАМИ игроку
        await player.Connection.SendMessage($"══════════════════════════════════════════");
        await player.Connection.SendMessage($"🎭 СЛЕПОЙ КАРМАННИК: выбирайте карту у {target.Name}");
        await player.Connection.SendMessage($"══════════════════════════════════════════");
        await player.Connection.SendMessage($"У {target.Name} {target.Hand.Count} карт:");

        // Показываем номера карт (рубашками)
        for (int i = 0; i < target.Hand.Count; i++)
        {
            await player.Connection.SendMessage($"  {i}. ❓ [Скрытая карта]");
        }

        // Запрашиваем выбор
        await player.Connection.SendMessage($"══════════════════════════════════════════");
        await player.Connection.SendMessage($"Выберите номер карты (0-{target.Hand.Count - 1}):");
        await player.Connection.SendMessage($"💡 Используйте команду: steal [номер_карты]");
        await player.Connection.SendMessage($"📝 Пример: steal 2");
        await player.Connection.SendMessage($"⏰ У вас есть 30 секунд на выбор");
        await player.Connection.SendMessage($"══════════════════════════════════════════");

        // Создаем ожидание выбора
        var stealData = new PendingStealAction
        {
            SessionId = session.Id,
            Player = player,
            Target = target,
            CardIndices = cardIndices, // Используем индексы
            Timestamp = DateTime.UtcNow
        };

        // Сохраняем в статическом словаре
        _pendingSteals[session.Id] = stealData;

        // Таймер на 30 секунд
        _ = Task.Delay(30000).ContinueWith(async _ =>
        {
            if (_pendingSteals.TryGetValue(session.Id, out var pending) &&
                pending.Timestamp == stealData.Timestamp)
            {
                // Таймаут - берем случайную карту
                await HandleStealTimeout(session, pending);
            }
        });
    }

    private async Task HandleThreeOfAKind(GameSession session, Player player, List<int> cardIndices, string? targetData)
    {
        if (string.IsNullOrEmpty(targetData))
        {
            await player.Connection.SendMessage("❌ Укажите игрока и название карты!");
            throw new ArgumentException("Не указаны данные для целевой карты");
        }

        var parts = targetData.Split('|');
        if (parts.Length < 2 || !Guid.TryParse(parts[0].Trim(), out var targetId))
        {
            await player.Connection.SendMessage("❌ Некорректный формат данных!");
            throw new ArgumentException("Некорректный формат данных");
        }

        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            await player.Connection.SendMessage("❌ Некорректный целевой игрок!");
            throw new ArgumentException("Некорректный целевой игрок");
        }

        var requestedCardName = parts[1].Trim();

        // Ждем 5 секунд на карты "Нет"
        PlayNopeHandler.StartNopeWindow(session);
        await Task.Delay(5000);

        if (PlayNopeHandler.IsActionNoped(session.Id))
        {
            await session.BroadcastMessage("⚡ Время Рыбачить отменено картой НЕТ!");
            PlayNopeHandler.CleanupNopeWindow(session.Id);
            return; // Комбо отменено - карты НЕ сбрасываются
        }

        PlayNopeHandler.CleanupNopeWindow(session.Id);

        // Ищем карту по имени у целевого игрока
        var requestedCard = target.Hand.FirstOrDefault(c =>
            c.Name.Equals(requestedCardName, StringComparison.OrdinalIgnoreCase));

        // ВАЖНО: Сбрасываем карты комбо ВСЕГДА, даже если не нашли карту
        Console.WriteLine($"DEBUG HandleThreeOfAKind: Сбрасываем карты комбо (индексы: {string.Join(",", cardIndices)})");
        DiscardComboCards(session, player, cardIndices);

        if (requestedCard == null)
        {
            await session.BroadcastMessage($"🎣 {player.Name} пытался взять карту '{requestedCardName}' у {target.Name}, но такой карты нет!");

            // Обновляем руку игрока (карты уже сброшены)
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
            return;
        }

        // Забираем карту у цели
        target.Hand.Remove(requestedCard);

        // Добавляем украденную карту
        player.AddToHand(requestedCard);

        await session.BroadcastMessage($"🎣 {player.Name} взял карту '{requestedCard.Name}' у {target.Name} используя Время Рыбачить!");

        // Обновляем руки обоих игроков
        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);
        await session.BroadcastGameState();
    }

    private async Task HandleFiveDifferent(GameSession session, Player player, List<int> cardIndices)
    {
        if (session.GameDeck.DiscardPile.Count == 0)
        {
            await session.BroadcastMessage("🗑️ Колода сброса пуста!");

            // Сбрасываем карты комбо
            DiscardComboCards(session, player, cardIndices);
            await session.BroadcastMessage($"{player.Name} использовал комбо, но сброс пуст!");
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
            return;
        }

        // Ждем 5 секунд на карты "Нет"
        PlayNopeHandler.StartNopeWindow(session);
        await Task.Delay(5000);

        if (PlayNopeHandler.IsActionNoped(session.Id))
        {
            await session.BroadcastMessage("⚡ Воровство из сброса отменено картой НЕТ!");
            PlayNopeHandler.CleanupNopeWindow(session.Id);
            return;
        }

        PlayNopeHandler.CleanupNopeWindow(session.Id);

        // Показываем карты в сбросе игроку
        var discardCards = session.GameDeck.DiscardPile
            .Select((card, index) => $"{index}. {card.Name}")
            .ToList();

        var discardInfo = string.Join("\n", discardCards);
        await player.Connection.SendMessage($"🗑️ Карты в сбросе:\n{discardInfo}");
        await player.Connection.SendMessage("💡 Введите номер карты, которую хотите взять:");
        await player.Connection.SendMessage($"📝 Используйте команду: takediscard [номер_карты]");
        await player.Connection.SendMessage($"💡 Пример: takediscard 2");
        await player.Connection.SendMessage($"⏰ У вас есть 30 секунд на выбор");

        // Создаем ожидание выбора - передаем индексы
        TakeFromDiscardHandler.CreatePendingAction(session, player, cardIndices);
    }

    private async Task HandleStealTimeout(GameSession session, PendingStealAction pending)
    {
        var player = pending.Player;
        var target = pending.Target;

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для кражи!");
            // Сбрасываем карты комбо
            DiscardComboCards(session, player, pending.CardIndices);
            _pendingSteals.TryRemove(session.Id, out _);
            return;
        }

        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);

        // Выполняем кражу с таймаутом
        await CompleteSteal(session, player, target, pending.CardIndices, stolenCardIndex, true);

        await session.BroadcastMessage($"(таймаут: выбрана случайная карта #{stolenCardIndex})");

        _pendingSteals.TryRemove(session.Id, out _);
    }

    private async Task CompleteSteal(GameSession session, Player player, Player target,
    List<int> cardIndices, int stolenCardIndex, bool isTimeout = false)
    {
        Console.WriteLine($"DEBUG CompleteSteal: Начинаем кражу");
        Console.WriteLine($"DEBUG: Игрок {player.Name} крадет у {target.Name}");
        Console.WriteLine($"DEBUG: Индексы карт для сброса: {string.Join(",", cardIndices)}");
        Console.WriteLine($"DEBUG: Карт в руке игрока до: {player.Hand.Count}");
        Console.WriteLine($"DEBUG: Карт в руке цели до: {target.Hand.Count}");

        if (stolenCardIndex < 0 || stolenCardIndex >= target.Hand.Count)
        {
            Console.WriteLine($"DEBUG: Неверный индекс кражи: {stolenCardIndex}");
            return;
        }

        var stolenCard = target.Hand[stolenCardIndex];
        Console.WriteLine($"DEBUG: Карта для кражи: {stolenCard.Name} (тип: {stolenCard.Type})");

        // Убираем карту у целевого игрока
        target.Hand.RemoveAt(stolenCardIndex);
        Console.WriteLine($"DEBUG: Карта удалена у цели. Осталось карт: {target.Hand.Count}");

        // Добавляем карту игроку
        player.AddToHand(stolenCard);
        Console.WriteLine($"DEBUG: Карта добавлена игроку. Теперь карт: {player.Hand.Count}");

        // Сбрасываем использованные карты комбо ПО ИНДЕКСАМ
        Console.WriteLine($"DEBUG: Сбрасываем карты комбо...");
        DiscardComboCards(session, player, cardIndices);

        var timeoutMsg = isTimeout ? " (таймаут)" : "";
        await session.BroadcastMessage($"🎭 {player.Name} украл карту '{stolenCard.Name}' у {target.Name} используя Слепой Карманник!{timeoutMsg}");

        // Обновляем руки обоих игроков
        Console.WriteLine($"DEBUG: Обновляем руки игроков...");
        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);
        await session.BroadcastGameState();

        Console.WriteLine($"DEBUG CompleteSteal: Завершено");
    }

    // Статический метод для завершения кражи извне (из StealCardHandler)
    public static async Task<bool> TryCompleteSteal(GameSession session, Player player, int cardIndex)
    {
        if (!_pendingSteals.TryGetValue(session.Id, out var pending))
            return false;

        if (pending.Player != player)
            return false;

        var target = pending.Target;

        if (cardIndex < 0 || cardIndex >= target.Hand.Count)
        {
            await player.Connection.SendMessage($"❌ Неверный номер карты! У {target.Name} только {target.Hand.Count} карт (0-{target.Hand.Count - 1})");
            return false;
        }

        // Выполняем кражу
        var handler = new UseComboHandler();
        await handler.CompleteSteal(session, player, target, pending.CardIndices, cardIndex);

        // Убираем из ожидания
        _pendingSteals.TryRemove(session.Id, out _);
        return true;
    }
}

public class PendingStealAction
{
    public required Guid SessionId { get; set; }
    public required Player Player { get; set; }
    public required Player Target { get; set; }
    public required List<int> CardIndices { get; set; } // Используем индексы
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}