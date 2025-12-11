using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayCard)]
public class PlayCardHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, // <-- Изменено
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var data = Encoding.UTF8.GetString(payload);
        var parts = data.Split(':');

        if (parts.Length < 3 || !Guid.TryParse(parts[0], out var gameId) ||
            !Guid.TryParse(parts[1], out var playerId))
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

        var player = session.GetPlayerById(playerId);
        if (player == null || player.Connection != sender)
        {
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        // Проверяем, может ли игрок играть карту
        if (!session.TurnManager.CanPlayCard() || session.CurrentPlayer != player)
        {
            await sender.SendError(CommandResponse.NotYourTurn);
            return;
        }

        if (!int.TryParse(parts[2], out var cardIndex) || cardIndex < 0 || cardIndex >= player.Hand.Count)
        {
            await sender.SendError(CommandResponse.CardNotFound);
            return;
        }

        var card = player.Hand[cardIndex];

        try
        {
            // Регистрируем начало действия для карты "Нет"
            if (card.Type != CardType.Nope && card.Type != CardType.ExplodingKitten && card.Type != CardType.Defuse)
            {
                PlayNopeHandler.StartNopeWindow(session);
            }

            // Обработка карты
            switch (card.Type)
            {
                case CardType.ExplodingKitten:
                    await HandleExplodingKitten(session, player, card);
                    break;

                case CardType.Attack:
                    await HandleAttack(session, player, card, parts.Length > 3 ? parts[3] : null);
                    break;

                case CardType.Skip:
                    await HandleSkip(session, player, card);
                    break;

                case CardType.Favor:
                    if (parts.Length < 4)
                    {
                        await sender.SendError(CommandResponse.InvalidAction);
                        return;
                    }
                    await HandleFavor(session, player, card, Guid.Parse(parts[3]));
                    break;

                case CardType.Shuffle:
                    await HandleShuffle(session, player, card);
                    break;

                case CardType.SeeTheFuture:
                    await HandleSeeTheFutureHandler(session, player, card); // Переименовали метод
                    break;

                case CardType.Nope:
                    await HandleNopeCard(session, player, card); // Переименовали метод
                    break;

                default:
                    if (card.IsCatCard)
                    {
                        await HandleCatCard(session, player, card, parts);
                    }
                    else
                    {
                        await sender.SendError(CommandResponse.InvalidAction);
                    }
                    break;
            }

            // Убираем карту из руки
            player.Hand.RemoveAt(cardIndex);
            session.GameDeck.Discard(card);

            // Регистрируем сыгранную карту
            session.TurnManager.CardPlayed(card);

            // Отправляем подтверждение
            await session.BroadcastMessage($"{player.Name} сыграл: {card.Name}");

            // Если после этой карты можно играть еще - сообщаем игроку
            if (session.TurnManager.CanPlayAnotherCard())
            {
                await player.Connection.SendMessage("Вы можете сыграть еще карту или взять карту из колоды (draw)");
            }
            else if (session.TurnManager.AttackPlayed)
            {
                // Если сыграли Attack - ход завершается без взятия карты
                await player.Connection.SendMessage("Attack сыгран! Ход завершается без взятия карты.");

                // Переходим к следующему игроку
                session.NextPlayer();
                if (session.State != GameState.GameOver)
                {
                    await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer!.Name}");
                    await session.CurrentPlayer!.Connection.SendMessage("Ваш ход!");
                }
            }
            else if (!session.TurnManager.HasDrawnCard)
            {
                // Если не сыграли Attack и не взяли карту - напоминаем взять карту
                await player.Connection.SendMessage("Вы должны взять карту из колоды! Команда: draw");
            }

            // Обновляем состояние
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при игре карты: {ex.Message}");
        }
    }

    private async Task HandleExplodingKitten(GameSession session, Player player, Card card)
    {
        await player.Connection.SendMessage("Взрывного Котенка нельзя сыграть из руки!");
        throw new InvalidOperationException("Cannot play Exploding Kitten from hand");
    }

    private async Task HandleAttack(GameSession session, Player player, Card card, string? targetPlayerId)
    {
        session.State = GameState.ResolvingAction;

        Player? target = null;
        if (!string.IsNullOrEmpty(targetPlayerId) && Guid.TryParse(targetPlayerId, out var targetId))
        {
            target = session.GetPlayerById(targetId);
        }

        // Ждем 5 секунд на карты "Нет"
        await Task.Delay(5000);

        // Проверяем, не было ли отменено картой "Нет"
        if (PlayNopeHandler.IsActionNoped(session.Id))
        {
            await session.BroadcastMessage("⚡ Атака отменена картой НЕТ!");
            PlayNopeHandler.CleanupNopeWindow(session.Id);
            session.State = GameState.PlayerTurn;
            return;
        }

        // Атака успешна - заканчиваем ход без взятия карты
        await session.BroadcastMessage($"⚔️ {player.Name} атаковал! Ход заканчивается.");

        // Применяем эффект атаки
        await ApplyAttackEffect(session, player, target);

        PlayNopeHandler.CleanupNopeWindow(session.Id);
        session.State = GameState.PlayerTurn;
    }

    private async Task ApplyAttackEffect(GameSession session, Player attacker, Player? target)
    {
        // Определяем игрока, который будет ходить дважды
        Player? attackTarget = target;
        if (attackTarget == null || !attackTarget.IsAlive)
        {
            // Если цель не указана или нежива, берем следующего живого игрока
            var nextIndex = session.CurrentPlayerIndex;
            var attempts = 0;

            do
            {
                nextIndex = (nextIndex + 1) % session.Players.Count;
                attackTarget = session.Players[nextIndex];
                attempts++;

                if (attempts > session.Players.Count)
                {
                    await session.BroadcastMessage("Нет живых игроков для атаки!");
                    return;
                }
            }
            while (!attackTarget.IsAlive);
        }

        // Целевой игрок получает +1 дополнительный ход (всего будет ходить дважды)
        attackTarget.ExtraTurns += 1;
        await session.BroadcastMessage($"{attackTarget.Name} ходит дважды из-за атаки!");

        // Ход завершается, переходим к следующему игроку
        session.NextPlayer();
    }

    private async Task HandleSkip(GameSession session, Player player, Card card)
    {
        await session.BroadcastMessage($"{player.Name} пропускает ход.");

        // Skip не заканчивает ход - игрок все равно должен взять карту
        await player.Connection.SendMessage("⚠️ Вы пропустили ход, но все равно должны взять карту из колоды!");
        await player.Connection.SendMessage("Используйте команду: draw");

        // Ничего не делаем - игрок продолжает ход и должен взять карту
    }

    private async Task HandleFavor(GameSession session, Player player, Card card, Guid targetId)
    {
        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            throw new ArgumentException("Некорректный целевой игрок");
        }

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для одолжения.");
            return;
        }

        session.State = GameState.ResolvingAction;
        await target.Connection.SendMessage($"{player.Name} просит у вас карту в одолжение!");
        await target.Connection.SendPlayerHand(target);
        await target.Connection.SendMessage("Введите номер карты, которую хотите отдать:");

        // В реальной реализации нужно дождаться ответа от target
        // Для простоты берем случайную карту
        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);
        var stolenCard = target.Hand[stolenCardIndex];
        target.Hand.RemoveAt(stolenCardIndex);
        player.AddToHand(stolenCard);

        await session.BroadcastMessage($"{player.Name} взял карту у {target.Name}");

        session.State = GameState.PlayerTurn;
    }

    private async Task HandleShuffle(GameSession session, Player player, Card card)
    {
        session.GameDeck.ShuffleDeck();
        await session.BroadcastMessage($"{player.Name} перемешал колоду.");
    }
    private async Task HandleSeeTheFutureHandler(GameSession session, Player player, Card card)
    {
        // Проверяем, достаточно ли карт в колоде
        if (!session.GameDeck.CanPeek(3))
        {
            await player.Connection.SendMessage("В колоде меньше 3 карт!");

            // Показываем сколько есть
            var available = Math.Min(3, session.GameDeck.CardsRemaining);
            if (available > 0)
            {
                var availableCards = session.GameDeck.PeekTop(available); // Изменили имя переменной
                var messageBuilder = new StringBuilder(); // Изменили имя переменной
                messageBuilder.AppendLine($"В колоде только {available} карт:");

                for (int i = 0; i < availableCards.Count; i++)
                {
                    messageBuilder.AppendLine($"{i + 1}. {availableCards[i].Name}");
                }

                await player.Connection.SendMessage(messageBuilder.ToString());
            }

            return;
        }

        // Берем 3 верхние карты (не удаляя их из колоды)
        var futureCards = session.GameDeck.PeekTop(3);

        if (futureCards.Count == 0)
        {
            await player.Connection.SendMessage("Колода пуста!");
            return;
        }

        // Формируем сообщение с картами
        var message = new StringBuilder();
        message.AppendLine("Три верхние карты колоды:");

        for (int i = 0; i < futureCards.Count; i++)
        {
            var topCard = futureCards[i];
            message.AppendLine($"{i + 1}. {topCard.Name}");
        }

        // Отправляем только текущему игроку
        await player.Connection.SendMessage(message.ToString());

        // Уведомляем других игроков, что игрок заглянул в будущее
        await session.BroadcastMessage($"{player.Name} заглянул в будущее.");

        // Согласно правилам - нельзя менять порядок карт
        await player.Connection.SendMessage("Карты оставлены в изначальном порядке.");
    }

    private async Task HandleNopeCard(GameSession session, Player player, Card card)
    {
        // Nope можно играть в любой момент
        if (!PlayNopeHandler.HasActiveNopeWindow(session.Id))
        {
            await player.Connection.SendMessage("Нет активных действий для отмены!");
            return;
        }

        // Используем статический метод PlayNopeHandler для обработки карты Nope
        // Для этого игрок должен отправить отдельную команду PlayNope
        await player.Connection.SendMessage("Для использования карты НЕТ используйте команду: nope [ID_игры]");
    }

    private async Task HandleCatCard(GameSession session, Player player, Card card, string[] parts)
    {
        // Обработка комбо с картами котов
        if (parts.Length > 3 && int.TryParse(parts[3], out var comboType))
        {
            await HandleCombo(session, player, card, comboType, parts.Length > 4 ? parts[4] : null);
        }
        else
        {
            await player.Connection.SendMessage("Карты котов можно играть только в комбо!");
        }
    }

    private async Task HandleCombo(GameSession session, Player player, Card card, int comboType, string? targetPlayerId)
    {
        // Ждем 5 секунд на карты "Нет"
        await Task.Delay(5000);

        if (PlayNopeHandler.IsActionNoped(session.Id))
        {
            await session.BroadcastMessage("⚡ Комбо отменено картой НЕТ!");
            PlayNopeHandler.CleanupNopeWindow(session.Id);
            return;
        }

        switch (comboType)
        {
            case 2: // Две одинаковые - слепой карманник
                if (!string.IsNullOrEmpty(targetPlayerId) && Guid.TryParse(targetPlayerId, out var targetId1))
                {
                    var target1 = session.GetPlayerById(targetId1);
                    if (target1 != null && target1 != player && target1.IsAlive && target1.Hand.Count > 0)
                    {
                        var random = new Random();
                        var stolenIndex = random.Next(target1.Hand.Count);
                        var stolenCard = target1.Hand[stolenIndex];
                        target1.Hand.RemoveAt(stolenIndex);
                        player.AddToHand(stolenCard);

                        await session.BroadcastMessage($"{player.Name} украл карту у {target1.Name}!");
                    }
                }
                break;

            case 3: // Три одинаковые - время рыбачить
                if (!string.IsNullOrEmpty(targetPlayerId))
                {
                    var targetParts = targetPlayerId.Split('|');
                    if (targetParts.Length == 2 && Guid.TryParse(targetParts[0], out var targetId2))
                    {
                        var target2 = session.GetPlayerById(targetId2);
                        if (target2 != null && target2 != player && target2.IsAlive)
                        {
                            var requestedCardName = targetParts[1];
                            var requestedCard = target2.Hand.FirstOrDefault(c =>
                                c.Name.Equals(requestedCardName, StringComparison.OrdinalIgnoreCase));

                            if (requestedCard != null)
                            {
                                target2.Hand.Remove(requestedCard);
                                player.AddToHand(requestedCard);
                                await session.BroadcastMessage($"{player.Name} взял карту '{requestedCard.Name}' у {target2.Name}!");
                            }
                            else
                            {
                                await session.BroadcastMessage($"{player.Name} пытался взять карту '{requestedCardName}' у {target2.Name}, но такой карты нет!");
                            }
                        }
                    }
                }
                break;

            case 5: // Пять разных - воруй из колоды сброса
                if (session.GameDeck.DiscardPile.Count > 0)
                {
                    var takenCard = session.GameDeck.TakeFromDiscard(0);
                    player.AddToHand(takenCard);
                    await session.BroadcastMessage($"{player.Name} взял карту из колоды сброса!");
                }
                break;
        }

        PlayNopeHandler.CleanupNopeWindow(session.Id);
    }
}