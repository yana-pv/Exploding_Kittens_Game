using Server.Game.Models;
using Server.Game.Models.Actions;
using Server.Infrastructure;
using Shared.Models;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.UseCombo)]
public class UseComboHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, PendingStealAction> _pendingSteals = new();
    private static readonly ConcurrentDictionary<Guid, ComboAction> _comboActions = new();

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

        var cardIndices = parts[3].Split(',')
            .Where(s => int.TryParse(s, out _))
            .Select(s => int.Parse(s))
            .ToList();

        if (cardIndices.Count != comboType)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (cardIndices.Any(i => i < 0 || i >= player.Hand.Count))
        {
            await sender.SendError(CommandResponse.CardNotFound);
            return;
        }

        try
        {
            if (!ValidateCombo(player, comboType, cardIndices))
            {
                await sender.SendError(CommandResponse.InvalidAction);
                return;
            }

            var comboActionId = Guid.NewGuid();
            var comboAction = new ComboAction
            {
                SessionId = session.Id,
                PlayerId = player.Id,
                ComboType = comboType,
                CardIndices = cardIndices,
                TargetData = parts.Length > 4 ? parts[4] : null,
                Timestamp = DateTime.UtcNow
            };

            _comboActions[comboActionId] = comboAction;

            await session.BroadcastMessage($"══════════════════════════════════════════");
            await session.BroadcastMessage($"🎭 {player.Name} играет комбо ({comboType} карты)!");
            await session.BroadcastMessage($"🚫 У вас есть 5 секунд чтобы сыграть карту НЕТ!");
            await session.BroadcastMessage($"Используйте: nope {session.Id} [ваш_ID] {comboActionId}");
            await session.BroadcastMessage($"══════════════════════════════════════════");

            await Task.Delay(5000);

            if (IsComboActionNoped(comboActionId))
            {
                await session.BroadcastMessage("⚡ Комбо отменено картой НЕТ!");
                _comboActions.TryRemove(comboActionId, out _);
                return; 
            }

            _comboActions.TryRemove(comboActionId, out _);

            switch (comboType)
            {
                case 2: 
                    await HandleTwoOfAKind(session, player, cardIndices, parts.Length > 4 ? parts[4] : null);
                    break;

                case 3: 
                    await HandleThreeOfAKind(session, player, cardIndices, parts.Length > 4 ? parts[4] : null);
                    break;

                case 5: 
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

        var handBefore = player.Hand.Select(c => c.Name).ToList();
        Console.WriteLine($"DEBUG: Рука до: {string.Join(", ", handBefore)}");

        var sortedIndices = cardIndices
            .OrderByDescending(i => i)
            .Distinct()
            .ToList();

        var discardedCards = new List<Card>();

        foreach (var index in sortedIndices)
        {
            if (index >= 0 && index < player.Hand.Count)
            {
                var card = player.Hand[index];

                discardedCards.Add(card);
                player.Hand.RemoveAt(index);
                session.GameDeck.Discard(card);
            }
            else
            {
            }
        }

        var handAfter = player.Hand.Select(c => c.Name).ToList();
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
            case 2: 
                return cards[0].Type == cards[1].Type ||
                       cards[0].IconId == cards[1].IconId;

            case 3: 
                return (cards[0].Type == cards[1].Type &&
                        cards[1].Type == cards[2].Type) ||
                       (cards[0].IconId == cards[1].IconId &&
                        cards[1].IconId == cards[2].IconId);

            case 5: 
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
            DiscardComboCards(session, player, cardIndices);
            await session.BroadcastMessage($"{player.Name} использовал Слепой Карманник, но {target.Name} не имеет карт!");
            return;
        }

        await player.Connection.SendMessage($"══════════════════════════════════════════");
        await player.Connection.SendMessage($"🎭 СЛЕПОЙ КАРМАННИК: выбирайте карту у {target.Name}");
        await player.Connection.SendMessage($"══════════════════════════════════════════");
        await player.Connection.SendMessage($"У {target.Name} {target.Hand.Count} карт:");

        for (int i = 0; i < target.Hand.Count; i++)
        {
            await player.Connection.SendMessage($"  {i}. ❓ [Скрытая карта]");
        }

        await player.Connection.SendMessage($"══════════════════════════════════════════");
        await player.Connection.SendMessage($"Выберите номер карты (0-{target.Hand.Count - 1}):");
        await player.Connection.SendMessage($"💡 Используйте команду: steal [номер_карты]");
        await player.Connection.SendMessage($"📝 Пример: steal 2");
        await player.Connection.SendMessage($"⏰ У вас есть 30 секунд на выбор");
        await player.Connection.SendMessage($"══════════════════════════════════════════");

        var stealData = new PendingStealAction
        {
            SessionId = session.Id,
            Player = player,
            Target = target,
            CardIndices = cardIndices,
            Timestamp = DateTime.UtcNow
        };

        _pendingSteals[session.Id] = stealData;

        _ = Task.Delay(30000).ContinueWith(async _ =>
        {
            if (_pendingSteals.TryGetValue(session.Id, out var pending) &&
                pending.Timestamp == stealData.Timestamp)
            {
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

        var requestedCard = target.Hand.FirstOrDefault(c =>
            c.Name.Equals(requestedCardName, StringComparison.OrdinalIgnoreCase));

        DiscardComboCards(session, player, cardIndices);

        if (requestedCard == null)
        {
            await session.BroadcastMessage($"🎣 {player.Name} пытался взять карту '{requestedCardName}' у {target.Name}, но такой карты нет!");

            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
            return;
        }

        target.Hand.Remove(requestedCard);

        player.AddToHand(requestedCard);

        await session.BroadcastMessage($"🎣 {player.Name} взял карту '{requestedCard.Name}' у {target.Name} используя Время Рыбачить!");

        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);
        await session.BroadcastGameState();
    }

    private async Task HandleFiveDifferent(GameSession session, Player player, List<int> cardIndices)
    {
        if (session.GameDeck.DiscardPile.Count == 0)
        {
            await session.BroadcastMessage("🗑️ Колода сброса пуста!");

            DiscardComboCards(session, player, cardIndices);
            await session.BroadcastMessage($"{player.Name} использовал комбо, но сброс пуст!");
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
            return;
        }

        var discardCards = session.GameDeck.DiscardPile
            .Select((card, index) => $"{index}. {card.Name}")
            .ToList();

        var discardInfo = string.Join("\n", discardCards);
        await player.Connection.SendMessage($"🗑️ Карты в сбросе:\n{discardInfo}");
        await player.Connection.SendMessage("💡 Введите номер карты, которую хотите взять:");
        await player.Connection.SendMessage($"📝 Используйте команду: takediscard [номер_карты]");
        await player.Connection.SendMessage($"💡 Пример: takediscard 2");
        await player.Connection.SendMessage($"⏰ У вас есть 30 секунд на выбор");

        TakeFromDiscardHandler.CreatePendingAction(session, player, cardIndices);
    }

    private async Task HandleStealTimeout(GameSession session, PendingStealAction pending)
    {
        var player = pending.Player;
        var target = pending.Target;

        _pendingSteals.TryRemove(session.Id, out _);

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для кражи!");

            DiscardComboCards(session, player, pending.CardIndices);

            await CompleteComboTurn(session, player);
            return;
        }

        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);

        await CompleteSteal(session, player, target, pending.CardIndices, stolenCardIndex, true);

        await session.BroadcastMessage($"(таймаут: выбрана случайная карта #{stolenCardIndex})");
    }

    private async Task CompleteSteal(GameSession session, Player player, Player target,
    List<int> cardIndices, int stolenCardIndex, bool isTimeout = false)
    {
        if (stolenCardIndex < 0 || stolenCardIndex >= target.Hand.Count)
        {
            return;
        }

        var stolenCard = target.Hand[stolenCardIndex];

        // 1. Сначала сбрасываем карты комбо
        DiscardComboCards(session, player, cardIndices);

        // 2. Затем забираем карту у цели
        target.Hand.RemoveAt(stolenCardIndex);

        // 3. Добавляем карту игроку
        player.AddToHand(stolenCard);

        var timeoutMsg = isTimeout ? " (таймаут)" : "";
        await session.BroadcastMessage($"🎭 {player.Name} украл карту '{stolenCard.Name}' у {target.Name} используя Слепой Карманник!{timeoutMsg}");

        // Обновляем руки обоих игроков
        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);

        // Завершаем ход после комбо
        await CompleteComboTurn(session, player);

        await session.BroadcastGameState();
    }

    private async Task CompleteComboTurn(GameSession session, Player player)
    {
        if (session.CurrentPlayer == player)
        {
            await player.Connection.SendMessage("🎭 Комбо завершено! Вы можете продолжить ход:");
            await player.Connection.SendMessage("• Сыграть еще карту (play [номер])");
            await player.Connection.SendMessage("• Взять карту из колоды (draw)");
            await player.Connection.SendMessage("• Завершить ход (end)");
        }
    }

    // Метод для проверки, отменено ли комбо Нетом
    private bool IsComboActionNoped(Guid comboActionId)
    {
        return false;
    }

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

        var handler = new UseComboHandler();
        await handler.CompleteSteal(session, player, target, pending.CardIndices, cardIndex);

        _pendingSteals.TryRemove(session.Id, out _);
        return true;
    }
}
