using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
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

        // Получаем сессию напрямую из менеджера
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

                    // Попробуем распарсить как Guid
                    if (Guid.TryParse(parts[3], out var favorTargetId))
                    {
                        await HandleFavor(session, player, card, favorTargetId);
                    }
                    else if (int.TryParse(parts[3], out var playerIndex))
                    {
                        // Используем индекс игрока
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
                    await HandleSeeTheFutureHandler(session, player, card);
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

            // Убираем карту из руки
            player.Hand.RemoveAt(cardIndex);
            session.GameDeck.Discard(card);

            // Регистрируем сыгранную карту
            session.TurnManager.CardPlayed(card);

            // Отправляем подтверждение
            await session.BroadcastMessage($"{player.Name} сыграл: {card.Name}");

            // Если карта завершает ход (Skip/Attack) - завершаем ход
            if (shouldEndTurn)
            {
                // Используем TurnManager для корректного завершения хода
                await session.TurnManager.CompleteTurnAsync();

                if (session.State != GameState.GameOver && session.CurrentPlayer != null)
                {
                    await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
                    await session.CurrentPlayer.Connection.SendMessage("Ваш ход!");
                }
            }
            else if (session.TurnManager.CanPlayAnotherCard())
            {
                // Если можно играть еще карты - сообщаем игроку
                await player.Connection.SendMessage("Вы можете сыграть еще карту или взять карту из колоды (draw)");
            }
            else if (!session.TurnManager.HasDrawnCard)
            {
                // Если не сыграли Skip/Attack и не взяли карту - напоминаем взять карту
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

            // Атака отменена - игрок продолжает ход
            await player.Connection.SendMessage("Атака отменена! Продолжайте ваш ход.");
            return;
        }

        // Атака успешна - заканчиваем ход БЕЗ взятия карты
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

        // НЕ переходим к следующему игроку здесь!
        // Это сделает TurnManager.CompleteTurnAsync() в основном методе
        // session.NextPlayer(); // УБРАТЬ ЭТУ СТРОЧКУ
    }

    private async Task HandleSkip(GameSession session, Player player, Card card)
    {
        await session.BroadcastMessage($"{player.Name} пропускает ход.");

        // Сообщаем игроку
        await player.Connection.SendMessage("Вы пропустили ход. Ход завершается без взятия карты.");
    }

    private async Task HandleFavor(GameSession session, Player player, Card card, Guid targetId)
    {
        Console.WriteLine($"DEBUG HandleFavor: Начинаем обработку для игрока {player.Name}");
        Console.WriteLine($"DEBUG: Цель ID: {targetId}");

        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            Console.WriteLine($"DEBUG: Некорректный целевой игрок");
            throw new ArgumentException("Некорректный целевой игрок");
        }

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для одолжения.");
            Console.WriteLine($"DEBUG: У цели нет карт");
            return;
        }

        Console.WriteLine($"DEBUG: У цели {target.Name} есть {target.Hand.Count} карт");

        // Запоминаем информацию о pending действии
        session.State = GameState.ResolvingAction;
        session.PendingFavor = new GameSession.PendingFavorAction
        {
            Requester = player,
            Target = target,
            Card = card,
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine($"DEBUG: Создан PendingFavor. Requester: {player.Name}, Target: {target.Name}");

        try
        {
            // Отправляем целевому игроку запрос на выбор карты
            await target.Connection.SendMessage($"══════════════════════════════════════════");
            await target.Connection.SendMessage($"🎭 {player.Name} просит у вас карту в одолжение!");
            await target.Connection.SendMessage($"══════════════════════════════════════════");

            // Показываем руку
            await target.Connection.SendPlayerHand(target);

            // Отправляем инструкцию с ПРАВИЛЬНОЙ командой
            await target.Connection.SendMessage($"💡 Используйте команду: favor {session.Id} {target.Id} [номер_карты]");
            await target.Connection.SendMessage($"📝 Пример: favor {session.Id} {target.Id} 0");
            await target.Connection.SendMessage($"⏰ У вас есть 30 секунд на выбор карты");
            await target.Connection.SendMessage($"══════════════════════════════════════════");

            Console.WriteLine($"DEBUG: Сообщение отправлено цели {target.Name}");

            // Ставим таймер на ответ
            _ = Task.Delay(30000).ContinueWith(async _ =>
            {
                Console.WriteLine($"DEBUG: Проверяем таймаут Favor для {target.Name}");

                if (session.State == GameState.ResolvingAction &&
                    session.PendingFavor != null &&
                    session.PendingFavor.Target == target &&
                    session.PendingFavor.Timestamp == session.PendingFavor.Timestamp) // проверяем тот же самый запрос
                {
                    Console.WriteLine($"DEBUG: Таймаут Favor для {target.Name}");
                    // Таймаут - берем случайную карту
                    await HandleFavorTimeout(session, player, target);
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG: Ошибка отправки сообщения цели: {ex.Message}");
            // Если не удалось отправить сообщение, сбрасываем pending действие
            session.PendingFavor = null;
            session.State = GameState.PlayerTurn;
        }
    }

    private async Task HandleFavorTimeout(GameSession session, Player requester, Player target)
    {
        Console.WriteLine($"DEBUG HandleFavorTimeout: Начинаем таймаут");

        if (session.State != GameState.ResolvingAction ||
            session.PendingFavor == null ||
            session.PendingFavor.Target != target)
        {
            Console.WriteLine($"DEBUG: Нет активного запроса или запрос уже обработан");
            return;
        }

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для одолжения.");
            Console.WriteLine($"DEBUG: У цели нет карт");
            session.PendingFavor = null;
            session.State = GameState.PlayerTurn;
            return;
        }

        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);
        var stolenCard = target.Hand[stolenCardIndex];

        Console.WriteLine($"DEBUG: Таймаут - берем случайную карту #{stolenCardIndex}: {stolenCard.Name}");

        // Убираем карту у целевого игрока
        target.Hand.RemoveAt(stolenCardIndex);

        // Добавляем карту запрашивающему игроку
        requester.AddToHand(stolenCard);

        await session.BroadcastMessage($"{requester.Name} взял случайную карту у {target.Name} (таймаут)!");

        // Очищаем pending действие
        session.PendingFavor = null;
        session.State = GameState.PlayerTurn;

        // Обновляем руки обоих игроков
        await target.Connection.SendPlayerHand(target);
        await requester.Connection.SendPlayerHand(requester);
        await session.BroadcastGameState();

        Console.WriteLine($"DEBUG HandleFavorTimeout: Завершено");
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
            case 2: // Две одинаковые - слепой карманник (случайная карта)
                await HandleTwoOfAKindCombo(session, player, targetPlayerId);
                break;

            case 3: // Три одинаковые - время рыбачить (конкретная карта)
                await HandleThreeOfAKindCombo(session, player, targetPlayerId);
                break;

            case 5: // Пять разных - воруй из колоды сброса
                await HandleFiveDifferentCombo(session, player);
                break;
        }

        PlayNopeHandler.CleanupNopeWindow(session.Id);
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

        // Берем СЛУЧАЙНУЮ карту у целевого игрока
        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);
        var stolenCard = target.Hand[stolenCardIndex];

        // Убираем карту у целевого игрока
        target.Hand.RemoveAt(stolenCardIndex);

        // Добавляем карту игроку
        player.AddToHand(stolenCard);

        await session.BroadcastMessage($"🎭 {player.Name} украл СЛУЧАЙНУЮ карту у {target.Name}!");
        await session.BroadcastMessage($"📤 У {target.Name} взята карта: {stolenCard.Name}");

        // Обновляем руки обоих игроков
        await target.Connection.SendPlayerHand(target);
        await player.Connection.SendPlayerHand(player);
    }

    private async Task HandleThreeOfAKindCombo(GameSession session, Player player, string? targetPlayerId)
    {
        if (string.IsNullOrEmpty(targetPlayerId))
        {
            await player.Connection.SendMessage("❌ Укажите игрока и название карты в формате: playerId|cardName!");
            throw new ArgumentException("Не указаны данные для целевой карты");
        }

        var parts = targetPlayerId.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var targetId))
        {
            await player.Connection.SendMessage("❌ Некорректный формат данных! Используйте: playerId|cardName");
            throw new ArgumentException("Некорректный формат данных");
        }

        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            await player.Connection.SendMessage("❌ Некорректный целевой игрок!");
            throw new ArgumentException("Некорректный целевой игрок");
        }

        var requestedCardName = parts[1];

        // Ищем карту по имени у целевого игрока
        var requestedCard = target.Hand.FirstOrDefault(c =>
            c.Name.Equals(requestedCardName, StringComparison.OrdinalIgnoreCase));

        if (requestedCard == null)
        {
            await session.BroadcastMessage($"🎣 {player.Name} пытался взять карту '{requestedCardName}' у {target.Name}, но такой карты нет!");
            return;
        }

        // Забираем КОНКРЕТНУЮ карту
        target.Hand.Remove(requestedCard);
        player.AddToHand(requestedCard);

        await session.BroadcastMessage($"🎣 {player.Name} взял карту '{requestedCard.Name}' у {target.Name}!");

        // Обновляем руки обоих игроков
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

        // Показываем карты в сбросе игроку
        var discardCards = session.GameDeck.DiscardPile
            .Select((card, index) => $"{index}. {card.Name}")
            .ToList();

        var discardInfo = string.Join("\n", discardCards);
        await player.Connection.SendMessage($"🗑️ Карты в сбросе:\n{discardInfo}");

        // Для простоты берем самую верхнюю карту из сброса
        // В полной реализации нужно дать игроку выбрать
        if (session.GameDeck.DiscardPile.Count > 0)
        {
            var takenCard = session.GameDeck.TakeFromDiscard(0); // Берем верхнюю карту
            player.AddToHand(takenCard);

            await session.BroadcastMessage($"🎨 {player.Name} взял карту '{takenCard.Name}' из колоды сброса!");

            await player.Connection.SendPlayerHand(player);
        }
    }
}