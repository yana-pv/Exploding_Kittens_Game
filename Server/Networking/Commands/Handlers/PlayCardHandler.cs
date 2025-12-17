using Server.Game.Enums;
using Server.Game.Models;
using Server.Game.Services;
using Server.Infrastructure;
using System.Net.Sockets;
using System.Text;
using static Server.Game.Models.GameSession;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayCard)]
public class PlayCardHandler : ICommandHandler
{
    // Для обработки Нетов (временное решение)
    private static readonly Dictionary<Guid, bool> _actionNopedStatus = new();
    private static readonly Dictionary<Guid, DateTime> _actionTimestamps = new();
    private static readonly Dictionary<Guid, string> _actionDescriptions = new();

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
            // Флаг для карт, которые завершают ход
            bool shouldEndTurn = false;

            // Обработка карты
            switch (card.Type)
            {
                case CardType.ExplodingKitten:
                    await HandleExplodingKitten(session, player, card);
                    break;

                case CardType.Attack:
                    await HandleAttack(session, player, card, parts.Length > 3 ? parts[3] : null);
                    shouldEndTurn = true; // Attack завершает ход
                    break;

                case CardType.Skip:
                    await HandleSkip(session, player, card);
                    shouldEndTurn = true; // Skip завершает ход
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

            // Убираем карту из руки и сбрасываем
            player.Hand.RemoveAt(cardIndex);
            session.GameDeck.Discard(card);

            // Регистрируем сыгранную карту
            session.TurnManager.CardPlayed(card);

            // Отправляем подтверждение
            await session.BroadcastMessage($"{player.Name} сыграл: {card.Name}");

            // Если карта завершает ход (Skip/Attack)
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

    private async Task HandleExplodingKitten(GameSession session, Player player, Card card)
    {
        await player.Connection.SendMessage("Взрывного Котенка нельзя сыграть из руки!");
        throw new InvalidOperationException("Cannot play Exploding Kitten from hand");
    }

    private async Task HandleAttack(GameSession session, Player player, Card card, string? targetPlayerId)
    {
        Console.WriteLine($"DEBUG HandleAttack: игрок {player.Name}, ExtraTurns={player.ExtraTurns}");

        session.State = GameState.ResolvingAction;

        Player? target = null;
        if (!string.IsNullOrEmpty(targetPlayerId) && Guid.TryParse(targetPlayerId, out var targetId))
        {
            target = session.GetPlayerById(targetId);
            Console.WriteLine($"DEBUG HandleAttack: цель указана - {target?.Name}");
        }

        // Создаем действие для возможных Нетов
        var attackActionId = Guid.NewGuid();

        // ИСПРАВЛЕНО: Добавляем параметр isCurrentPlayer
        bool isCurrentPlayer = session.CurrentPlayer == player;
        PlayNopeHandler.RegisterAttackAction(session.Id, attackActionId, player.Name, target?.Name, isCurrentPlayer);

        // Уведомляем всех игроков
        await session.BroadcastMessage($"══════════════════════════════════════════");
        await session.BroadcastMessage($"⚔️ {player.Name} играет 'Атаковать'!");
        if (target != null)
        {
            await session.BroadcastMessage($"🎯 Цель: {target.Name}");
        }

        // Разные сообщения в зависимости от очереди хода
        await session.BroadcastMessage($"🚫 Время для карт НЕТ:");
        if (isCurrentPlayer)
        {
            // Игрок на своем ходу - может играть Нет в любое время
            await session.BroadcastMessage($"• {player.Name} (на своем ходу): может сыграть Нет в любое время");
            await session.BroadcastMessage($"• Остальные игроки: 5 секунд с момента этого сообщения");

            // ИСПРАВЛЕНО: Не ждем 5 секунд для игрока на своем ходу
            // Атака сразу применяется, но действие остается для возможного отмены
            Console.WriteLine($"DEBUG: Игрок на своем ходу, не ждем 5 секунд");
        }
        else
        {
            // Игрок не на своем ходу - у всех 5 секунд
            await session.BroadcastMessage($"• Все игроки: 5 секунд с момента этого сообщения");

            // Ждем 5 секунд на карты "Нет" от других игроков
            await Task.Delay(5000);
        }

        // ИСПРАВЛЕНО: Проверяем Неты только если игрок не на своем ходу
        // (игрок на своем ходу может отменить позже)
        if (!isCurrentPlayer && PlayNopeHandler.IsActionNoped(attackActionId))
        {
            Console.WriteLine($"DEBUG HandleAttack: атака отменена Нетом");
            await session.BroadcastMessage("⚡ Атака отменена картой НЕТ!");

            PlayNopeHandler.CleanupAction(attackActionId, session.Id);
            session.State = GameState.PlayerTurn;

            // Сбрасываем флаг _turnEnded, чтобы игрок мог продолжать
            ResetTurnEndedFlag(session.TurnManager);

            // Сбрасываем флаги Skip/Attack
            ResetAttackSkipFlags(session.TurnManager);

            await player.Connection.SendMessage("Атака отменена! Продолжайте ваш ход.");

            // Если у игрока были ExtraTurns, он продолжает дополнительный ход
            if (player.ExtraTurns > 0)
            {
                await session.BroadcastMessage($"{player.Name} продолжает дополнительный ход после отмененной атаки");
            }

            return;
        }

        Console.WriteLine($"DEBUG HandleAttack: атака НЕ отменена");

        // Проверяем, является ли игрок жертвой предыдущей атаки
        bool isCounterAttack = player.ExtraTurns > 0;
        Console.WriteLine($"DEBUG HandleAttack: isCounterAttack={isCounterAttack}");

        if (isCounterAttack)
        {
            // КОНТРАТАКА
            Console.WriteLine($"DEBUG HandleAttack: КОНТРАТАКА");

            await session.BroadcastMessage($"⚔️ {player.Name} контратакует! Ход заканчивается.");

            // Сбрасываем ExtraTurns у текущего игрока
            player.ExtraTurns = 0;

            // Находим следующего живого игрока
            var nextPlayer = FindNextAlivePlayer(session, player);
            if (nextPlayer != null)
            {
                Console.WriteLine($"DEBUG HandleAttack: следующий игрок для контратаки - {nextPlayer.Name}");

                // Переносим атаку на следующего игрока
                nextPlayer.ExtraTurns = 1;

                await session.BroadcastMessage($"⚔️ {nextPlayer.Name} ходит дважды из-за контратаки!");
                session.GameLog.Add($"{player.Name} контратаковал {nextPlayer.Name}");
            }
        }
        else
        {
            // ОБЫЧНАЯ АТАКА
            Console.WriteLine($"DEBUG HandleAttack: ОБЫЧНАЯ АТАКА");

            await session.BroadcastMessage($"⚔️ {player.Name} атаковал! Ход заканчивается.");

            // Применяем эффект атаки
            await ApplyAttackEffect(session, player, target);
        }

        // ИСПРАВЛЕНО: Очищаем действие только если игрок не на своем ходу
        // (действие игрока на своем ходу очистится через таймер или когда он сделает следующее действие)
        if (!isCurrentPlayer)
        {
            PlayNopeHandler.CleanupAction(attackActionId, session.Id);
        }

        session.State = GameState.PlayerTurn;
    }

    // Новый метод для сброса флага _turnEnded
    private void ResetTurnEndedFlag(TurnManager turnManager)
    {
        try
        {
            // Используем reflection для доступа к приватному полю
            var turnManagerType = turnManager.GetType();
            var turnEndedField = turnManagerType.GetField("_turnEnded",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (turnEndedField != null)
            {
                turnEndedField.SetValue(turnManager, false);
                Console.WriteLine($"DEBUG: _turnEnded сброшен");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG: Ошибка сброса _turnEnded: {ex.Message}");
        }
    }

    // Новый метод для сброса флагов Skip/Attack
    private void ResetAttackSkipFlags(TurnManager turnManager)
    {
        try
        {
            var turnManagerType = turnManager.GetType();

            // Сбрасываем _skipPlayed
            var skipPlayedField = turnManagerType.GetField("_skipPlayed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (skipPlayedField != null)
            {
                skipPlayedField.SetValue(turnManager, false);
            }

            // Сбрасываем _attackPlayed
            var attackPlayedField = turnManagerType.GetField("_attackPlayed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (attackPlayedField != null)
            {
                attackPlayedField.SetValue(turnManager, false);
            }

            Console.WriteLine($"DEBUG: флаги Skip/Attack сброшены");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG: Ошибка сброса флагов: {ex.Message}");
        }
    }

    // Методы для обработки Нетов
    private void RegisterActionForNope(Guid actionId, string playerName, string? targetName)
    {
        var description = targetName != null
            ? $"{playerName} атакует {targetName}"
            : $"{playerName} играет Атаковать";

        _actionDescriptions[actionId] = description;
        _actionTimestamps[actionId] = DateTime.UtcNow;
        _actionNopedStatus[actionId] = false;

        // Автоочистка через 10 секунд
        Task.Delay(10000).ContinueWith(_ =>
        {
            _actionDescriptions.Remove(actionId);
            _actionTimestamps.Remove(actionId);
            _actionNopedStatus.Remove(actionId);
        });
    }

    private bool IsActionNoped(Guid actionId)
    {
        return _actionNopedStatus.TryGetValue(actionId, out var noped) && noped;
    }

    private void CleanupAction(Guid actionId)
    {
        _actionDescriptions.Remove(actionId);
        _actionTimestamps.Remove(actionId);
        _actionNopedStatus.Remove(actionId);
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

    private async Task ApplyAttackEffect(GameSession session, Player attacker, Player? target)
    {
        Console.WriteLine($"DEBUG ApplyAttackEffect: атакующий={attacker.Name}, цель={target?.Name}");

        // Определяем игрока, который будет ходить дважды
        Player? attackTarget = target;

        if (attackTarget == null || !attackTarget.IsAlive)
        {
            attackTarget = FindNextAlivePlayer(session, attacker);
        }

        if (attackTarget == null)
        {
            await session.BroadcastMessage("❌ Нет живых игроков для атаки!");
            return;
        }

        // Проверяем, не пытается ли игрок атаковать самого себя
        if (attackTarget == attacker)
        {
            attackTarget = FindNextAlivePlayer(session, attacker);

            if (attackTarget == null || attackTarget == attacker)
            {
                await session.BroadcastMessage("❌ Нельзя атаковать самого себя!");
                return;
            }
        }

        // Помечаем цель как атакованную
        attackTarget.ExtraTurns = 1;

        Console.WriteLine($"DEBUG ApplyAttackEffect: {attackTarget.Name} помечен как атакованный");

        await session.BroadcastMessage($"⚔️ {attackTarget.Name} ходит дважды из-за атаки {attacker.Name}!");

        session.GameLog.Add($"{attacker.Name} атаковал {attackTarget.Name}");
    }

    private async Task HandleSkip(GameSession session, Player player, Card card)
    {
        await session.BroadcastMessage($"{player.Name} пропускает ход.");
        await player.Connection.SendMessage("Вы пропустили ход. Ход завершается без взятия карты.");
    }

    private async Task HandleFavor(GameSession session, Player player, Card card, Guid targetId)
    {
        Console.WriteLine($"DEBUG HandleFavor: игрок {player.Name}, цель ID: {targetId}");

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

            // Таймер на ответ
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
        session.GameDeck.ShuffleDeck();
        await session.BroadcastMessage($"{player.Name} перемешал колоду.");
    }

    private async Task HandleSeeTheFuture(GameSession session, Player player, Card card)
    {
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

        await player.Connection.SendMessage(message.ToString());
        await session.BroadcastMessage($"{player.Name} заглянул в будущее.");
    }

    private async Task HandleNopeCard(GameSession session, Player player, Card card)
    {
        Console.WriteLine($"DEBUG HandleNopeCard: игрок {player.Name} играет Нет");

        // Получаем активное действие для этой сессии
        var activeActionId = PlayNopeHandler.GetActiveActionForSession(session.Id);

        if (!activeActionId.HasValue)
        {
            await player.Connection.SendMessage("❌ Нет активных действий для отмены!");
            return;
        }

        // Проверяем, можно ли играть Нет
        // Игрок на своем ходу может отменять свои действия в любое время
        if (!PlayNopeHandler.CanPlayNopeOnAction(activeActionId.Value, session.CurrentPlayer == player))
        {
            await player.Connection.SendMessage("❌ Время для Нета истекло!");
            return;
        }

        // Проверяем, не использовал ли уже этот игрок Nope
        if (PlayNopeHandler.HasPlayerAlreadyNoped(activeActionId.Value, player))
        {
            await player.Connection.SendMessage("❌ Вы уже использовали Nope на это действие!");
            return;
        }

        // Проверяем, есть ли у игрока карта Nope
        if (!player.HasCard(CardType.Nope))
        {
            await player.Connection.SendMessage("❌ У вас нет карты Нет!");
            return;
        }

        try
        {
            // Убираем карту Nope из руки
            var nopeCard = player.RemoveCard(CardType.Nope);
            if (nopeCard != null)
            {
                session.GameDeck.Discard(nopeCard);
            }

            // Регистрируем использование Нета
            PlayNopeHandler.RegisterNopeForAction(activeActionId.Value, player);

            var description = PlayNopeHandler.GetActionDescription(activeActionId.Value);
            await session.BroadcastMessage($"🚫 {player.Name} сказал НЕТ на: {description}!");

            // Немедленно проверяем, было ли действие отменено
            if (PlayNopeHandler.IsActionNoped(activeActionId.Value))
            {
                // Применяем эффект отмены
                await ApplyNopeEffect(session, activeActionId.Value, description);
            }

            // Обновляем руку игрока
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке карты Нет: {ex.Message}");
            await player.Connection.SendMessage($"Ошибка при игре карты Нет: {ex.Message}");
        }
    }

    // Добавляем метод для применения эффекта Нета
    private async Task ApplyNopeEffect(GameSession session, Guid actionId, string actionDescription)
    {
        if (actionDescription.Contains("атакует") || actionDescription.Contains("Атаковать"))
        {
            await session.BroadcastMessage("⚡ Атака отменена картой НЕТ!");

            // Сбрасываем состояние хода
            ResetTurnEndedFlag(session.TurnManager);
            ResetAttackSkipFlags(session.TurnManager);

            // Очищаем действие
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
        // Создаем действие для Нетов
        var comboActionId = Guid.NewGuid();

        // ИСПРАВЛЕНО: Используем новый метод регистрации из PlayNopeHandler
        PlayNopeHandler.RegisterComboAction(session.Id, comboActionId, player.Name, comboType);

        // Уведомляем всех игроков
        await session.BroadcastMessage($"══════════════════════════════════════════");
        await session.BroadcastMessage($"🎭 {player.Name} играет комбо ({comboType} карты)!");

        // НОВОЕ: Разные сообщения в зависимости от очереди хода
        await session.BroadcastMessage($"🚫 Время для карт НЕТ:");
        if (session.CurrentPlayer == player)
        {
            // Игрок на своем ходу - может играть Нет в любое время
            await session.BroadcastMessage($"• {player.Name} (на своем ходу): может сыграть Нет в любое время");
            await session.BroadcastMessage($"• Остальные игроки: 5 секунд с момента этого сообщения");
        }
        else
        {
            // Игрок не на своем ходу - у всех 5 секунд
            await session.BroadcastMessage($"• Все игроки: 5 секунд с момента этого сообщения");
        }

        await session.BroadcastMessage($"Используйте: nope {session.Id} [ваш_ID] {comboActionId}");
        await session.BroadcastMessage($"══════════════════════════════════════════");

        // Ждем 5 секунд на карты "Нет" от других игроков
        await Task.Delay(5000);

        // Проверяем, было ли отменено картой "Нет"
        if (PlayNopeHandler.IsActionNoped(comboActionId))
        {
            await session.BroadcastMessage("⚡ Комбо отменено картой НЕТ!");

            // ИСПРАВЛЕНО: Используем новый метод очистки
            PlayNopeHandler.CleanupAction(comboActionId, session.Id);
            return; // Комбо отменено - карты НЕ сбрасываются
        }

        // Очищаем действие
        PlayNopeHandler.CleanupAction(comboActionId, session.Id);

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
        await session.BroadcastMessage($"📤 У {target.Name} взята карта: {stolenCard.Name}");

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