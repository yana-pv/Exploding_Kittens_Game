using Server.Game.Models;
using Server.Game.Models.Actions;
using Server.Infrastructure;
using Shared.Models;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayDefuse)]
public class PlayDefuseHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, PendingExplosion> _pendingExplosions = new();

    public async Task Invoke(Socket sender, GameSessionManager sessionManager,
        byte[]? payload = null, CancellationToken ct = default)
    {
        Console.WriteLine($"DEBUG PlayDefuseHandler: получен запрос defuse в {DateTime.UtcNow:HH:mm:ss.fff}");

        if (payload == null || payload.Length == 0)
        {
            Console.WriteLine($"DEBUG: пустой payload");
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var data = Encoding.UTF8.GetString(payload);
        Console.WriteLine($"DEBUG: данные = {data}");

        var parts = data.Split(':');

        // Формат: gameId:playerId
        if (parts.Length < 2 || !Guid.TryParse(parts[0], out var gameId) ||
            !Guid.TryParse(parts[1], out var playerId))
        {
            Console.WriteLine($"DEBUG: неверный формат данных");
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        Console.WriteLine($"DEBUG: gameId = {gameId}, playerId = {playerId}");

        var session = sessionManager.GetSession(gameId);
        if (session == null)
        {
            Console.WriteLine($"DEBUG: игра не найдена");
            await sender.SendError(CommandResponse.GameNotFound);
            return;
        }

        var player = session.GetPlayerById(playerId);
        if (player == null || player.Connection != sender)
        {
            Console.WriteLine($"DEBUG: игрок не найден или не совпадает сокет");
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        Console.WriteLine($"DEBUG: найден игрок {player.Name}");

        // Проверяем, есть ли активный взрыв для этого игрока
        if (!_pendingExplosions.TryGetValue(player.Id, out var pending))
        {
            Console.WriteLine($"DEBUG: нет активного взрыва для {player.Name}");
            await player.Connection.SendMessage("❌ Нет активного взрыва для обезвреживания!");
            return;
        }

        Console.WriteLine($"DEBUG: найден активный взрыв, timestamp = {pending.Timestamp:HH:mm:ss.fff}");

        if (pending.Session.Id != session.Id)
        {
            Console.WriteLine($"DEBUG: несоответствие сессий");
            await player.Connection.SendMessage("❌ Несоответствие сессий!");
            return;
        }

        // Увеличиваем время проверки
        var timeSinceExplosion = DateTime.UtcNow - pending.Timestamp;
        Console.WriteLine($"DEBUG: прошло {timeSinceExplosion.TotalSeconds:F2} секунд");

        if (timeSinceExplosion.TotalSeconds > 35)
        {
            Console.WriteLine($"DEBUG: слишком поздно ({timeSinceExplosion.TotalSeconds:F2} секунд)");
            await player.Connection.SendMessage("❌ Слишком поздно! Время для обезвреживания истекло.");
            return;
        }

        // Проверяем, есть ли у игрока карта "Обезвредить"
        if (!player.HasCard(CardType.Defuse))
        {
            Console.WriteLine($"DEBUG: у игрока {player.Name} нет карты Обезвредить");
            await player.Connection.SendMessage("❌ У вас нет карты Обезвредить!");
            await HandlePlayerElimination(session, player, pending.KittenCard, true);
            return;
        }

        Console.WriteLine($"DEBUG: игрок имеет карту Обезвредить, отменяем таймер...");

        // Отменяем таймер
        pending.TimeoutToken?.Cancel();

        // Убираем из словаря сразу
        _pendingExplosions.TryRemove(player.Id, out _);

        // Выполняем обезвреживание
        await CompleteDefuse(session, player, pending.KittenCard);
    }

    private async Task CompleteDefuse(GameSession session, Player player, Card kittenCard)
    {
        try
        {
            Console.WriteLine($"DEBUG CompleteDefuse: начинаем обезвреживание для {player.Name}");

            // Убираем карту "Обезвредить" из руки игрока
            var defuseCard = player.RemoveCard(CardType.Defuse);
            if (defuseCard == null)
            {
                Console.WriteLine($"DEBUG: не удалось найти карту Обезвредить в руке");
                await player.Connection.SendMessage("❌ Не удалось найти карту Обезвредить!");
                return;
            }

            // Убираем Взрывного Котенка из руки игрока
            var explodingKitten = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);
            if (explodingKitten == null)
            {
                Console.WriteLine($"DEBUG: Взрывной котенок не найден в руке");
                await player.Connection.SendMessage("❌ Взрывной котенок не найден в вашей руке!");
                return;
            }
            player.Hand.Remove(explodingKitten);

            // Сбрасываем карту "Обезвредить"
            session.GameDeck.Discard(defuseCard);

            // Возвращаем Взрывного Котенка в колоду в СЛУЧАЙНОЕ место
            var random = new Random();

            // Получаем текущий размер колоды
            int deckSize = session.GameDeck.CardsRemaining;
            Console.WriteLine($"DEBUG: текущий размер колоды = {deckSize}");

            int position;

            // Если колода пуста
            if (deckSize == 0)
            {
                position = 0;
                session.GameDeck.InsertCard(explodingKitten, position);
                Console.WriteLine($"DEBUG: котенок возвращен в пустую колоду");
            }
            else
            {
                // Выбираем случайную позицию от 0 до deckSize включительно
                // 0 - самый верх (следующая карта)
                // deckSize - самый низ (последняя карта)
                position = random.Next(0, deckSize + 1);
                session.GameDeck.InsertCard(explodingKitten, position);

                Console.WriteLine($"DEBUG: котенок возвращен в колоду на позицию {position} (всего карт: {deckSize + 1})");
            }

            // Только общее сообщение, без указания позиции
            await session.BroadcastMessage($"✅ {player.Name} обезвредил Взрывного Котенка!");
            await session.BroadcastMessage($"Котенок возвращен в колоду в неизвестное место.");

            await player.Connection.SendMessage($"🎯 Вы успешно обезвредили Взрывного Котенка!");
            await player.Connection.SendMessage($"Котенок возвращен в колоду в случайное место.");

            // Завершаем ход
            if (session.TurnManager != null)
            {
                session.TurnManager.CardDrawn();
                await session.TurnManager.CompleteTurnAsync();
            }

            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();

            Console.WriteLine($"DEBUG CompleteDefuse: успешно завершено для {player.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG CompleteDefuse: ошибка - {ex.Message}");
            await player.Connection.SendMessage($"Ошибка при обезвреживании котенка: {ex.Message}");
        }
    }

    public static void RegisterExplosion(GameSession session, Player player)
    {
        Console.WriteLine($"DEBUG RegisterExplosion: вызывается для {player.Name}");

        // Проверяем, что котенок действительно в руке
        Console.WriteLine($"DEBUG: Рука игрока содержит {player.Hand.Count} карт:");
        foreach (var card in player.Hand)
        {
            Console.WriteLine($"  - {card.Name} ({card.Type})");
        }

        var kittenCard = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);

        if (kittenCard == null)
        {
            Console.WriteLine($"DEBUG RegisterExplosion: ОШИБКА! Котенок не найден в руке игрока!");
            Console.WriteLine($"DEBUG: Player.Hand.Count = {player.Hand.Count}");
            return;
        }

        Console.WriteLine($"DEBUG: Найден котенок: {kittenCard.Name}");

        // Удаляем старый взрыв, если есть
        _pendingExplosions.TryRemove(player.Id, out var oldPending);
        oldPending?.TimeoutToken?.Cancel();

        var pending = new PendingExplosion
        {
            Player = player,
            Session = session,
            KittenCard = kittenCard,
            Timestamp = DateTime.UtcNow,
            TimeoutToken = new CancellationTokenSource()
        };

        _pendingExplosions[player.Id] = pending;

        Console.WriteLine($"DEBUG: взрыв зарегистрирован в {pending.Timestamp:HH:mm:ss.fff}");

        // Запускаем таймер
        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"DEBUG: запущен таймер 30 секунд для {player.Name}");
                await Task.Delay(30000, pending.TimeoutToken.Token);

                // После задержки проверяем
                if (_pendingExplosions.TryGetValue(player.Id, out var current) &&
                    current.Timestamp == pending.Timestamp)
                {
                    Console.WriteLine($"DEBUG: таймаут! {player.Name} не успел обезвредить");
                    await HandleTimeoutElimination(session, player);
                }
                else
                {
                    Console.WriteLine($"DEBUG: таймер сработал, но взрыв уже обработан");
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"DEBUG: таймер отменен для {player.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: ошибка в таймере - {ex.Message}");
            }
        });
    }

    private static async Task HandleTimeoutElimination(GameSession session, Player player)
    {
        Console.WriteLine($"DEBUG HandleTimeoutElimination: обработка таймаута для {player.Name}");

        // Проверяем, все еще ли игрок жив
        if (!player.IsAlive)
        {
            Console.WriteLine($"DEBUG: игрок уже не жив");
            return;
        }

        // Убираем из словаря
        _pendingExplosions.TryRemove(player.Id, out _);

        var eliminationMessage = "💥 Время вышло! Вы не успели обезвредить котенка и выбываете из игры.";
        await player.Connection.SendMessage(eliminationMessage);

        await session.BroadcastMessage($"💥 {player.Name} не успел обезвредить котенка и выбывает из игры!");

        // Находим котенка в руке
        var kittenCard = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);
        if (kittenCard != null)
        {
            session.EliminatePlayer(player);
        }
        else
        {
            Console.WriteLine($"DEBUG: котенок не найден в руке при таймауте");
        }

        await session.BroadcastGameState();
    }

    public static bool HasPendingExplosion(Player player)
    {
        return _pendingExplosions.ContainsKey(player.Id);
    }

    private async Task HandlePlayerElimination(GameSession session, Player player, Card kittenCard, bool fromDefuseHandler = false)
    {
        Console.WriteLine($"DEBUG HandlePlayerElimination: устранение игрока {player.Name}");

        if (_pendingExplosions.TryGetValue(player.Id, out var pending))
        {
            pending.TimeoutToken?.Cancel();
            _pendingExplosions.TryRemove(player.Id, out _);
        }

        if (player.IsAlive)
        {
            var eliminationMessage = "💥 Вы выбываете из игры!";
            await player.Connection.SendMessage(eliminationMessage);

            await session.BroadcastMessage($"💥 {player.Name} выбывает из игры!");

            session.EliminatePlayer(player);

            if (!fromDefuseHandler && session.State != GameState.GameOver)
            {
                session.NextPlayer();
                if (session.CurrentPlayer != null)
                {
                    await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
                    await session.CurrentPlayer.Connection.SendMessage("Ваш ход!");
                }
            }

            await session.BroadcastGameState();
        }
    }
}