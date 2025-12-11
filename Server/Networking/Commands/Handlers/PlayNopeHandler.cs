using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayNope)]
public class PlayNopeHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, List<Player>> _pendingActions = new();
    private static readonly ConcurrentDictionary<Guid, DateTime> _actionTimestamps = new();

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

        if (parts.Length < 2 || !Guid.TryParse(parts[0], out var gameId) ||
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

        // Проверяем, есть ли у игрока карта Nope
        if (!player.HasCard(CardType.Nope))
        {
            await sender.SendError(CommandResponse.CardNotFound);
            return;
        }

        // Проверяем, есть ли активное действие для Nope
        if (!_pendingActions.ContainsKey(session.Id) ||
            !_actionTimestamps.ContainsKey(session.Id) ||
            (DateTime.UtcNow - _actionTimestamps[session.Id]).TotalSeconds > 10)
        {
            await player.Connection.SendMessage("Нет активных действий для отмены!");
            return;
        }

        try
        {
            // Проверяем, не использовал ли уже этот игрок Nope на это действие
            if (_pendingActions[session.Id].Contains(player))
            {
                await player.Connection.SendMessage("Вы уже использовали Nope на это действие!");
                return;
            }

            // Добавляем игрока в список использовавших Nope
            _pendingActions[session.Id].Add(player);

            // Убираем карту Nope из руки игрока
            var nopeCard = player.RemoveCard(CardType.Nope);
            if (nopeCard != null)
            {
                session.GameDeck.Discard(nopeCard);
            }

            await session.BroadcastMessage($"🚫 {player.Name} сказал НЕТ!");

            // Если 2 или больше игроков сказали Nope, действие отменяется
            if (_pendingActions[session.Id].Count >= 2)
            {
                await CancelPendingAction(session);
            }
            else
            {
                // Даем другим игрокам еще 5 секунд на использование Nope
                _actionTimestamps[session.Id] = DateTime.UtcNow;

                var remainingTime = 10 - (DateTime.UtcNow - _actionTimestamps[session.Id]).TotalSeconds;
                if (remainingTime > 0)
                {
                    await session.BroadcastMessage($"У других игроков есть {Math.Ceiling(remainingTime)} секунд чтобы сказать НЕТ!");
                }
            }

            // Обновляем руку игрока
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при игре карты НЕТ: {ex.Message}");
        }
    }

    public static void StartNopeWindow(GameSession session)
    {
        _pendingActions[session.Id] = new List<Player>();
        _actionTimestamps[session.Id] = DateTime.UtcNow;

        // Автоматически очищаем через 10 секунд
        Task.Delay(10000).ContinueWith(_ =>
        {
            if (_pendingActions.ContainsKey(session.Id) &&
                _actionTimestamps.ContainsKey(session.Id) &&
                (DateTime.UtcNow - _actionTimestamps[session.Id]).TotalSeconds >= 10)
            {
                CleanupNopeWindow(session.Id);
            }
        });
    }

    public static bool HasActiveNopeWindow(Guid sessionId)
    {
        return _pendingActions.ContainsKey(sessionId) &&
               _actionTimestamps.ContainsKey(sessionId) &&
               (DateTime.UtcNow - _actionTimestamps[sessionId]).TotalSeconds <= 10;
    }

    public static bool IsActionNoped(Guid sessionId)
    {
        return _pendingActions.ContainsKey(sessionId) &&
               _pendingActions[sessionId].Count > 0;
    }

    public static void CleanupNopeWindow(Guid sessionId)
    {
        _pendingActions.TryRemove(sessionId, out _);
        _actionTimestamps.TryRemove(sessionId, out _);
    }

    private async Task CancelPendingAction(GameSession session)
    {
        await session.BroadcastMessage("⚡ Действие отменено несколькими картами НЕТ!");

        // В зависимости от состояния игры, возвращаемся к предыдущему состоянию
        if (session.State == GameState.ResolvingAction)
        {
            session.State = GameState.PlayerTurn;
            await session.BroadcastMessage($"{session.CurrentPlayer!.Name}, продолжайте ваш ход.");
        }

        CleanupNopeWindow(session.Id);
    }
}