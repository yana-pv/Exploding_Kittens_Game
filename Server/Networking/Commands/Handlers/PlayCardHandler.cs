using Server.Game.Models;
using Server.Game.Models.Actions;
using Server.Game.Services;
using Server.Infrastructure;
using Shared.Models;
using System.Net.Sockets;
using System.Text;
using static Server.Game.Models.GameSession;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayCard)]
public class PlayCardHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, byte[]? payload = null, CancellationToken ct = default)
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
            bool shouldEndTurn = false;

            switch (card.Type)
            {
                case CardType.ExplodingKitten:
                    await HandleExplodingKitten(session, player, card);
                    break;

                case CardType.Attack:
                    await HandleAttack(session, player, card, parts.Length > 3 ? parts[3] : null);
                    shouldEndTurn = true;
                    break;

                case CardType.Skip:
                    await HandleSkip(session, player, card);
                    shouldEndTurn = true;
                    break;

                case CardType.Favor:
                    if (parts.Length < 4)
                    {
                        await sender.SendError(CommandResponse.InvalidAction);
                        return;
                    }

                    if (Guid.TryParse(parts[3], out var favorTargetId))
                    {
                        await HandleFavor(session, player, card, favorTargetId);
                    }
                    else if (int.TryParse(parts[3], out var playerIndex))
                    {
                        var targetPlayer = session.Players
                            .Where(p => p.IsAlive && p != player)
                            .ElementAtOrDefault(playerIndex);

                        if (targetPlayer == null)
                        {
                            await sender.SendError(CommandResponse.PlayerNotFound);
                            return;
                        }

                        await HandleFavor(session, player, card, targetPlayer.Id);
                    }
                    else
                    {
                        await sender.SendError(CommandResponse.InvalidAction);
                    }
                    break;

                case CardType.Shuffle:
                    await HandleShuffle(session, player, card);
                    break;

                case CardType.SeeTheFuture:
                    await HandleSeeTheFuture(session, player, card);
                    break;

                case CardType.Nope:
                    await HandleNopeCard(session, player, card);
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

            // Удаляем карту из руки и сбрасываем ее
            player.Hand.RemoveAt(cardIndex);
            session.GameDeck.Discard(card);
            session.TurnManager.CardPlayed(card);

            // Отправляем общее сообщение о сыгранной карте (только для определенных карт)
            await SendCardPlayedMessages(session, player, card, shouldEndTurn);

            if (shouldEndTurn)
            {
                await session.TurnManager.CompleteTurnAsync();

                if (session.State != GameState.GameOver && session.CurrentPlayer != null)
                {
                    await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
                    await session.CurrentPlayer.Connection.SendMessage("Ваш ход!");
                }
            }
            else if (session.TurnManager.CanPlayAnotherCard())
            {
                await player.Connection.SendMessage("Вы можете сыграть еще карту или взять карту из колоды (draw)");
            }
            else if (!session.TurnManager.HasDrawnCard)
            {
                await player.Connection.SendMessage("Вы должны взять карту из колоды! Команда: draw");
            }

            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при игре карты: {ex.Message}");
        }
    }

    private async Task SendCardPlayedMessages(GameSession session, Player player, Card card, bool shouldEndTurn)
    {
        // Для карты "Заглянуть в будущее" сообщения уже отправлены в HandleSeeTheFuture
        if (card.Type == CardType.SeeTheFuture)
        {
            return;
        }

        // Для карты "Одолжение" сообщения отправляются в HandleFavor
        if (card.Type == CardType.Favor)
        {
            // Одолжение отправляет свое сообщение отдельно
            return;
        }

        // Для карты "Нет" сообщения отправляются в HandleNopeCard
        if (card.Type == CardType.Nope)
        {
            return;
        }

        // Для карт "Атаковать" и "Пропустить" уже отправлены сообщения в соответствующих методах
        if (card.Type == CardType.Attack || card.Type == CardType.Skip)
        {
            return;
        }

        // Для карты "Перемешать" уже отправлено сообщение в HandleShuffle
        if (card.Type == CardType.Shuffle)
        {
            return;
        }

        // Для карт котов (комбо) сообщения отправляются в соответствующих методах
        if (card.IsCatCard)
        {
            return;
        }

        // Для всех остальных карт отправляем общее сообщение
        await session.BroadcastMessage($"{player.Name} сыграл: {card.Name}");
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

        var attackActionId = Guid.NewGuid();

        bool isCurrentPlayer = session.CurrentPlayer == player;
        PlayNopeHandler.RegisterAttackAction(session.Id, attackActionId, player.Name, target?.Name, isCurrentPlayer);

        await session.BroadcastMessage($"══════════════════════════════════════════");

        if (target != null)
        {
            await session.BroadcastMessage($"⚔️ {player.Name} атакует {target.Name}!");
        }
        else
        {
            await session.BroadcastMessage($"⚔️ {player.Name} играет 'Атаковать'!");
        }

        // Уведомляем, что можно играть "Нет"
        await session.BroadcastMessage($"🚫 Игроки могут сыграть карту НЕТ!");
        await session.BroadcastMessage($"Используйте команду: nope");
        await session.BroadcastMessage($"══════════════════════════════════════════");

        // Даем время на реакцию (необязательно, но для UX)
        await Task.Delay(3000); // 3 секунды для UX

        // Проверяем, не отменено ли действие
        if (PlayNopeHandler.IsActionNoped(attackActionId))
        {
            await session.BroadcastMessage("⚡ Атака отменена картой НЕТ!");

            PlayNopeHandler.CompleteAction(attackActionId, session.Id);
            session.State = GameState.PlayerTurn;

            ResetTurnEndedFlag(session.TurnManager);
            ResetAttackSkipFlags(session.TurnManager);

            await player.Connection.SendMessage("Атака отменена! Продолжайте ваш ход.");

            if (player.ExtraTurns > 0)
            {
                await session.BroadcastMessage($"{player.Name} продолжает дополнительный ход после отмененной атаки");
            }

            return;
        }

        // Завершаем действие (Nope больше нельзя играть)
        PlayNopeHandler.CompleteAction(attackActionId, session.Id);

        bool isCounterAttack = player.ExtraTurns > 0;

        if (isCounterAttack)
        {
            await session.BroadcastMessage($"⚔️ {player.Name} контратакует! Ход заканчивается.");

            player.ExtraTurns = 0;

            var nextPlayer = FindNextAlivePlayer(session, player);
            if (nextPlayer != null)
            {
                nextPlayer.ExtraTurns = 1;

                await session.BroadcastMessage($"⚔️ {nextPlayer.Name} ходит дважды из-за контратаки!");
                session.GameLog.Add($"{player.Name} контратаковал {nextPlayer.Name}");
            }
        }
        else
        {
            await session.BroadcastMessage($"⚔️ {player.Name} атаковал! Ход заканчивается.");
        }

        session.State = GameState.PlayerTurn;
    }

    private void ResetTurnEndedFlag(TurnManager turnManager)
    {
        var turnManagerType = turnManager.GetType();
        var turnEndedField = turnManagerType.GetField("_turnEnded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (turnEndedField != null)
        {
            turnEndedField.SetValue(turnManager, false);
        }
    }

    private void ResetAttackSkipFlags(TurnManager turnManager)
    {
        var turnManagerType = turnManager.GetType();

        var skipPlayedField = turnManagerType.GetField("_skipPlayed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (skipPlayedField != null)
        {
            skipPlayedField.SetValue(turnManager, false);
        }

        var attackPlayedField = turnManagerType.GetField("_attackPlayed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (attackPlayedField != null)
        {
            attackPlayedField.SetValue(turnManager, false);
        }
    }

    private Player? FindNextAlivePlayer(GameSession session, Player fromPlayer)
    {
        if (session.Players.Count == 0)
            return null;

        var players = session.Players;
        var startIndex = players.IndexOf(fromPlayer);

        if (startIndex == -1)
            return null;

        var attempts = 0;
        var currentIndex = startIndex;

        do
        {
            currentIndex = (currentIndex + 1) % players.Count;
            var candidate = players[currentIndex];
            attempts++;

            if (attempts > players.Count)
                return null;

            if (candidate.IsAlive)
                return candidate;

        } while (currentIndex != startIndex);

        return null;
    }

    private async Task HandleSkip(GameSession session, Player player, Card card)
    {
        var skipActionId = Guid.NewGuid();
        PlayNopeHandler.RegisterAttackAction(session.Id, skipActionId, player.Name,
            null, session.CurrentPlayer == player);

        await session.BroadcastMessage($"{player.Name} пропускает ход.");

        // Даем время на Nope
        await Task.Delay(2000);

        if (PlayNopeHandler.IsActionNoped(skipActionId))
        {
            await session.BroadcastMessage("⚡ Пропуск отменен картой НЕТ!");
            PlayNopeHandler.CompleteAction(skipActionId, session.Id);

            // Сбрасываем флаги TurnManager
            if (session.TurnManager != null)
            {
                var turnManagerType = typeof(TurnManager);
                var skipPlayedField = turnManagerType.GetField("_skipPlayed",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (skipPlayedField != null)
                {
                    skipPlayedField.SetValue(session.TurnManager, false);
                }

                var turnEndedField = turnManagerType.GetField("_turnEnded",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (turnEndedField != null)
                {
                    turnEndedField.SetValue(session.TurnManager, false);
                }
            }

            await player.Connection.SendMessage("Пропуск отменен! Продолжайте ваш ход.");
            return;
        }

        PlayNopeHandler.CompleteAction(skipActionId, session.Id);
        await player.Connection.SendMessage("Вы пропустили ход. Ход завершается без взятия карты.");
    }

    private async Task HandleFavor(GameSession session, Player player, Card card, Guid targetId)
    {
        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            throw new ArgumentException("Некорректный целевой игрок");
        }

        // Регистрируем действие для Nope
        var favorActionId = Guid.NewGuid();
        PlayNopeHandler.RegisterAttackAction(session.Id, favorActionId, player.Name,
            target.Name, session.CurrentPlayer == player);

        // Даем время на Nope
        await Task.Delay(2000);

        if (PlayNopeHandler.IsActionNoped(favorActionId))
        {
            await session.BroadcastMessage("⚡ Одолжение отменено картой НЕТ!");
            PlayNopeHandler.CompleteAction(favorActionId, session.Id);

            // Возвращаем карту в руку игрока
            player.AddToHand(card);
            await player.Connection.SendMessage("Одолжение отменено! Карта возвращена в вашу руку.");
            return;
        }

        PlayNopeHandler.CompleteAction(favorActionId, session.Id);

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для одолжения.");
            return;
        }

        session.State = GameState.ResolvingAction;
        session.PendingFavor = new PendingFavorAction
        {
            Requester = player,
            Target = target,
            Card = card,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await target.Connection.SendMessage($"══════════════════════════════════════════");
            await target.Connection.SendMessage($"🎭 {player.Name} просит у вас карту в одолжение!");
            await target.Connection.SendMessage($"══════════════════════════════════════════");

            await target.Connection.SendPlayerHand(target);

            await target.Connection.SendMessage($"💡 Используйте: favor {session.Id} {target.Id} [номер_карты]");
            await target.Connection.SendMessage($"📝 Пример: favor {session.Id} {target.Id} 0");
            await target.Connection.SendMessage($"⏰ У вас есть 30 секунд на выбор");
            await target.Connection.SendMessage($"══════════════════════════════════════════");

            _ = Task.Delay(30000).ContinueWith(async _ =>
            {
                if (session.State == GameState.ResolvingAction &&
                    session.PendingFavor != null &&
                    session.PendingFavor.Target == target)
                {
                    await HandleFavorTimeout(session, player, target);
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки сообщения цели: {ex.Message}");
            session.PendingFavor = null;
            session.State = GameState.PlayerTurn;
        }
    }

    private async Task HandleFavorTimeout(GameSession session, Player requester, Player target)
    {
        if (session.State != GameState.ResolvingAction ||
            session.PendingFavor == null ||
            session.PendingFavor.Target != target)
        {
            return;
        }

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для одолжения.");
            session.PendingFavor = null;
            session.State = GameState.PlayerTurn;
            return;
        }

        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);
        var stolenCard = target.Hand[stolenCardIndex];

        target.Hand.RemoveAt(stolenCardIndex);
        requester.AddToHand(stolenCard);

        await session.BroadcastMessage($"{requester.Name} взял случайную карту у {target.Name} (таймаут)!");

        session.PendingFavor = null;
        session.State = GameState.PlayerTurn;

        await target.Connection.SendPlayerHand(target);
        await requester.Connection.SendPlayerHand(requester);
        await session.BroadcastGameState();
    }

    private async Task HandleShuffle(GameSession session, Player player, Card card)
    {
        var shuffleActionId = Guid.NewGuid();
        PlayNopeHandler.RegisterAttackAction(session.Id, shuffleActionId, player.Name,
            null, session.CurrentPlayer == player);

        // Даем время на Nope
        await Task.Delay(2000);

        if (PlayNopeHandler.IsActionNoped(shuffleActionId))
        {
            await session.BroadcastMessage("⚡ Перемешивание отменено картой НЕТ!");
            PlayNopeHandler.CompleteAction(shuffleActionId, session.Id);

            // Возвращаем карту в руку игрока
            player.AddToHand(card);
            await player.Connection.SendMessage("Перемешивание отменено! Карта возвращена в вашу руку.");
            return;
        }

        PlayNopeHandler.CompleteAction(shuffleActionId, session.Id);

        session.GameDeck.ShuffleDeck();
        await session.BroadcastMessage($"{player.Name} перемешал колоду.");
    }

    private async Task HandleSeeTheFuture(GameSession session, Player player, Card card)
    {
        var seeFutureActionId = Guid.NewGuid();
        PlayNopeHandler.RegisterAttackAction(session.Id, seeFutureActionId, player.Name,
            null, session.CurrentPlayer == player);

        // Даем время на Nope
        await Task.Delay(2000);

        if (PlayNopeHandler.IsActionNoped(seeFutureActionId))
        {
            await session.BroadcastMessage("⚡ Заглянуть в будущее отменено картой НЕТ!");
            PlayNopeHandler.CompleteAction(seeFutureActionId, session.Id);

            // Возвращаем карту в руку игрока
            player.AddToHand(card);
            await player.Connection.SendMessage("Заглянуть в будущее отменено! Карта возвращена в вашу руку.");
            return;
        }

        PlayNopeHandler.CompleteAction(seeFutureActionId, session.Id);

        if (!session.GameDeck.CanPeek(3))
        {
            await player.Connection.SendMessage("В колоде меньше 3 карт!");
            return;
        }

        var futureCards = session.GameDeck.PeekTop(3);

        if (futureCards.Count == 0)
        {
            await player.Connection.SendMessage("Колода пуста!");
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("Три верхние карты колоды:");

        for (int i = 0; i < futureCards.Count; i++)
        {
            message.AppendLine($"{i + 1}. {futureCards[i].Name}");
        }

        // Отправляем результат только игроку, который сыграл карту
        await player.Connection.SendMessage(message.ToString());

        // Остальным игрокам отправляем только факт использования карты
        var broadcastMessage = $"{player.Name} заглянул в будущее.";
        await session.BroadcastToOthers(player, broadcastMessage);
    }

    private async Task HandleNopeCard(GameSession session, Player player, Card card)
    {
        var activeActionId = PlayNopeHandler.GetActiveActionForSession(session.Id);

        if (!activeActionId.HasValue)
        {
            await player.Connection.SendMessage("❌ Нет активных действий для отменяния!");
            return;
        }

        if (!PlayNopeHandler.CanPlayNopeOnAction(activeActionId.Value, session.CurrentPlayer == player))
        {
            await player.Connection.SendMessage("❌ Время для Нета истекло!");
            return;
        }

        if (PlayNopeHandler.HasPlayerAlreadyNoped(activeActionId.Value, player))
        {
            await player.Connection.SendMessage("❌ Вы уже использовали Nope на это действие!");
            return;
        }

        if (!player.HasCard(CardType.Nope))
        {
            await player.Connection.SendMessage("❌ У вас нет карты Нет!");
            return;
        }

        try
        {
            var nopeCard = player.RemoveCard(CardType.Nope);
            if (nopeCard != null)
            {
                session.GameDeck.Discard(nopeCard);
            }

            PlayNopeHandler.RegisterNopeForAction(activeActionId.Value, player);

            var description = PlayNopeHandler.GetActionDescription(activeActionId.Value);
            await session.BroadcastMessage($"🚫 {player.Name} сказал НЕТ на: {description}!");

            if (PlayNopeHandler.IsActionNoped(activeActionId.Value))
            {
                await ApplyNopeEffect(session, activeActionId.Value, description);
            }

            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await player.Connection.SendMessage($"Ошибка при игре карты Нет: {ex.Message}");
        }
    }

    private async Task ApplyNopeEffect(GameSession session, Guid actionId, string actionDescription)
    {
        if (actionDescription.Contains("атакует") || actionDescription.Contains("Атаковать"))
        {
            await session.BroadcastMessage("⚡ Атака отменена картой НЕТ!");

            ResetTurnEndedFlag(session.TurnManager);
            ResetAttackSkipFlags(session.TurnManager);

            PlayNopeHandler.CleanupAction(actionId, session.Id);
        }
    }

    private async Task HandleCatCard(GameSession session, Player player, Card card, string[] parts)
    {
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
        var comboActionId = Guid.NewGuid();

        // Получаем тип первой карты в комбо (это card.Type)
        var firstCardType = card.Type;

        PlayNopeHandler.RegisterComboAction(session.Id, comboActionId, player.Name, comboType, firstCardType);

        await session.BroadcastMessage($"══════════════════════════════════════════");
        await session.BroadcastMessage($"🎭 {player.Name} играет комбо ({comboType} карты)!");

        await session.BroadcastMessage($"🚫 Игроки могут сыграть карту НЕТ!");
        await session.BroadcastMessage($"Используйте команду: nope");
        await session.BroadcastMessage($"══════════════════════════════════════════");

        // Даем время на реакцию (необязательно, но для UX)
        await Task.Delay(3000); // 3 секунды для UX

        if (PlayNopeHandler.IsActionNoped(comboActionId))
        {
            await session.BroadcastMessage("⚡ Комбо отменено картой НЕТ!");
            PlayNopeHandler.CompleteAction(comboActionId, session.Id);

            // Возвращаем карты в руку игрока
            // Для этого нужно знать, какие карты были в комбо
            // В реальной реализации нужно сохранить индексы карт
            await player.Connection.SendMessage("Комбо отменено! Карты остаются в вашей руке.");
            return;
        }

        // Завершаем действие (Nope больше нельзя играть)
        PlayNopeHandler.CompleteAction(comboActionId, session.Id);

        switch (comboType)
        {
            case 2:
                await HandleTwoOfAKindCombo(session, player, targetPlayerId);
                break;
            case 3:
                await HandleThreeOfAKindCombo(session, player, targetPlayerId);
                break;
            case 5:
                await HandleFiveDifferentCombo(session, player);
                break;
        }
    }

    private async Task HandleTwoOfAKindCombo(GameSession session, Player player, string? targetPlayerId)
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
            return;
        }

        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);
        var stolenCard = target.Hand[stolenCardIndex];

        target.Hand.RemoveAt(stolenCardIndex);
        player.AddToHand(stolenCard);

        await session.BroadcastMessage($"🎭 {player.Name} украл СЛУЧАЙНУЮ карту у {target.Name}!");

        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);
    }

    private async Task HandleThreeOfAKindCombo(GameSession session, Player player, string? targetPlayerId)
    {
        if (string.IsNullOrEmpty(targetPlayerId))
        {
            await player.Connection.SendMessage("❌ Укажите игрока и название карты!");
            throw new ArgumentException("Не указаны данные для целевой карты");
        }

        var parts = targetPlayerId.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var targetId))
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

        var requestedCardName = parts[1];
        var requestedCard = target.Hand.FirstOrDefault(c =>
            c.Name.Equals(requestedCardName, StringComparison.OrdinalIgnoreCase));

        if (requestedCard == null)
        {
            await session.BroadcastMessage($"🎣 {player.Name} пытался взять карту '{requestedCardName}' у {target.Name}, но такой карты нет!");
            return;
        }

        target.Hand.Remove(requestedCard);
        player.AddToHand(requestedCard);

        await session.BroadcastMessage($"🎣 {player.Name} взял карту '{requestedCard.Name}' у {target.Name}!");

        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);
    }

    private async Task HandleFiveDifferentCombo(GameSession session, Player player)
    {
        if (session.GameDeck.DiscardPile.Count == 0)
        {
            await session.BroadcastMessage("🗑️ Колода сброса пуста!");
            return;
        }

        var discardCards = session.GameDeck.DiscardPile
            .Select((card, index) => $"{index}. {card.Name}")
            .ToList();

        var discardInfo = string.Join("\n", discardCards);
        await player.Connection.SendMessage($"🗑️ Карты в сбросе:\n{discardInfo}");

        if (session.GameDeck.DiscardPile.Count > 0)
        {
            var takenCard = session.GameDeck.TakeFromDiscard(0);
            player.AddToHand(takenCard);

            await session.BroadcastMessage($"🎨 {player.Name} взял карту '{takenCard.Name}' из колоды сброса!");
            await player.Connection.SendPlayerHand(player);
        }
    }
}