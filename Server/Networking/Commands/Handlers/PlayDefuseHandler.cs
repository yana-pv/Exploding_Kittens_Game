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
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var data = Encoding.UTF8.GetString(payload);

        var parts = data.Split(':');

        if (parts.Length < 2 || !Guid.TryParse(parts[0], out var gameId) ||
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


        // Проверяем, есть ли активный взрыв для этого игрока
        if (!_pendingExplosions.TryGetValue(player.Id, out var pending))
        {
            await player.Connection.SendMessage("❌ Нет активного взрыва для обезвреживания!");
            return;
        }

        if (pending.Session.Id != session.Id)
        {
            await player.Connection.SendMessage("❌ Несоответствие сессий!");
            return;
        }

        // Увеличиваем время проверки
        var timeSinceExplosion = DateTime.UtcNow - pending.Timestamp;

        if (timeSinceExplosion.TotalSeconds > 35)
        {
            await player.Connection.SendMessage("❌ Слишком поздно! Время для обезвреживания истекло.");
            return;
        }

        // Проверяем, есть ли у игрока карта "Обезвредить"
        if (!player.HasCard(CardType.Defuse))
        {
            await player.Connection.SendMessage("❌ У вас нет карты Обезвредить!");
            await HandlePlayerElimination(session, player, pending.KittenCard, true);
            return;
        }

        // Отменяем таймер
        pending.TimeoutToken?.Cancel();

        _pendingExplosions.TryRemove(player.Id, out _);

        // Выполняем обезвреживание
        await CompleteDefuse(session, player, pending.KittenCard);
    }

    private async Task CompleteDefuse(GameSession session, Player player, Card kittenCard)
    {
        try
        {
            var defuseCard = player.RemoveCard(CardType.Defuse);
            if (defuseCard == null)
            {
                await player.Connection.SendMessage("❌ Не удалось найти карту Обезвредить!");
                return;
            }

            var explodingKitten = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);
            if (explodingKitten == null)
            {
                await player.Connection.SendMessage("❌ Взрывной котенок не найден в вашей руке!");
                return;
            }
            player.Hand.Remove(explodingKitten);

            session.GameDeck.Discard(defuseCard);

            // Возвращаем Взрывного Котенка в колоду в случайное место
            var random = new Random();

            // Получаем текущий размер колоды
            int deckSize = session.GameDeck.CardsRemaining;
            int position;

            if (deckSize == 0)
            {
                position = 0;
                session.GameDeck.InsertCard(explodingKitten, position);
                Console.WriteLine($"DEBUG: котенок возвращен в пустую колоду");
            }
            else
            {
                position = random.Next(0, deckSize + 1);
                session.GameDeck.InsertCard(explodingKitten, position);
            }

            await session.BroadcastMessage($"✅ {player.Name} обезвредил Взрывного Котенка!");
            await session.BroadcastMessage($"Котенок возвращен в колоду в неизвестное место.");

            await player.Connection.SendMessage($"🎯 Вы успешно обезвредили Взрывного Котенка!");

            // Завершаем ход
            if (session.TurnManager != null)
            {
                session.TurnManager.CardDrawn();
                await session.TurnManager.CompleteTurnAsync();
            }

            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await player.Connection.SendMessage($"Ошибка при обезвреживании котенка: {ex.Message}");
        }
    }

    public static void RegisterExplosion(GameSession session, Player player)
    {
        foreach (var card in player.Hand)
        {
            Console.WriteLine($"  - {card.Name} ({card.Type})");
        }

        var kittenCard = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);

        if (kittenCard == null)
        {
            return;
        }


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

        // Запускаем таймер
        _ = Task.Run(async () =>
        {
            await Task.Delay(30000, pending.TimeoutToken.Token);

            if (_pendingExplosions.TryGetValue(player.Id, out var current) &&
               current.Timestamp == pending.Timestamp)
            {                 
                await HandleTimeoutElimination(session, player);
            }
        });
    }

    private static async Task HandleTimeoutElimination(GameSession session, Player player)
    {
        // Проверяем, все еще ли игрок жив
        if (!player.IsAlive)
        {
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

        await session.BroadcastGameState();
    }

    private async Task HandlePlayerElimination(GameSession session, Player player, Card kittenCard, bool fromDefuseHandler = false)
    {
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