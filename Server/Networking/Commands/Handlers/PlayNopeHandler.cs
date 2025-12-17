using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using Server.Networking;
using Server.Networking.Commands;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using static Server.Game.Models.GameSession;

[Command(Command.PlayNope)]
public class PlayNopeHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, PendingAction> _pendingActions = new();
    private static readonly ConcurrentDictionary<Guid, DateTime> _actionTimestamps = new();
    private static readonly ConcurrentDictionary<Guid, List<Player>> _actionNopes = new();
    private static readonly ConcurrentDictionary<Guid, string> _actionDescriptions = new();
    private static readonly ConcurrentDictionary<Guid, bool> _isCurrentPlayerAction = new();


    // Новый словарь для отслеживания текущего активного действия в сессии
    private static readonly ConcurrentDictionary<Guid, Guid> _sessionActiveAction = new();

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

        // Формат: gameId:playerId:actionId
        if (parts.Length < 3 || !Guid.TryParse(parts[0], out var gameId) ||
            !Guid.TryParse(parts[1], out var playerId) ||
            !Guid.TryParse(parts[2], out var actionId))
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

        // Проверяем, есть ли у игрока карта Nope
        if (!player.HasCard(CardType.Nope))
        {
            await sender.SendError(CommandResponse.CardNotFound);
            return;
        }

        // Проверяем, существует ли такое действие
        if (!_actionDescriptions.ContainsKey(actionId))
        {
            await player.Connection.SendMessage("❌ Это действие уже обработано или не существует!");
            return;
        }

        // НОВОЕ: Проверяем очередь хода
        if (!CanPlayNopeNow(session, player, actionId))
        {
            await player.Connection.SendMessage("❌ Нельзя сыграть Нет сейчас!");
            return;
        }

        // Проверяем, не использовал ли уже этот игрок Nope на это действие
        if (_actionNopes.TryGetValue(actionId, out var nopePlayers) &&
            nopePlayers.Any(p => p.Id == player.Id))
        {
            await player.Connection.SendMessage("❌ Вы уже использовали Nope на это действие!");
            return;
        }

        try
        {
            // Убираем карту Nope из руки игрока
            var nopeCard = player.RemoveCard(CardType.Nope);
            if (nopeCard != null)
            {
                session.GameDeck.Discard(nopeCard);
            }

            // Добавляем игрока в список использовавших Nope
            if (!_actionNopes.ContainsKey(actionId))
            {
                _actionNopes[actionId] = new List<Player>();
            }
            _actionNopes[actionId].Add(player);

            await session.BroadcastMessage($"🚫 {player.Name} сказал НЕТ на: {_actionDescriptions[actionId]}");

            // Если это первый Нет на это действие, обновляем таймер
            _actionTimestamps[actionId] = DateTime.UtcNow;

            // Обновляем руку игрока
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при игре карты НЕТ: {ex.Message}");
        }
    }

    // НОВЫЙ МЕТОД: Проверка возможности сыграть Нет
    private bool CanPlayNopeNow(GameSession session, Player player, Guid actionId)
    {
        // 1. Если сейчас ход игрока - можно играть Нет в любое время
        if (session.CurrentPlayer == player)
        {
            Console.WriteLine($"DEBUG: Игрок {player.Name} на своем ходу, может играть Нет");
            return true;
        }

        // 2. Если не ход игрока - проверяем время действия
        if (!_actionTimestamps.ContainsKey(actionId))
        {
            return false;
        }

        var timeSinceAction = DateTime.UtcNow - _actionTimestamps[actionId];

        // 3. Можно играть Нет только в течение 5 секунд после действия
        if (timeSinceAction.TotalSeconds <= 5)
        {
            Console.WriteLine($"DEBUG: Игрок {player.Name} не на своем ходу, но в пределах 5 секунд ({timeSinceAction.TotalSeconds:F1} сек)");
            return true;
        }

        Console.WriteLine($"DEBUG: Игрок {player.Name} не может играть Нет - прошло {timeSinceAction.TotalSeconds:F1} секунд");
        return false;
    }

    // НОВЫЙ МЕТОД: Регистрация действия атаки с учетом очереди хода
    public static void RegisterAttackAction(Guid sessionId, Guid actionId, string attackerName,
    string? targetName, bool isCurrentPlayer)
    {
        var description = targetName != null
            ? $"{attackerName} атакует {targetName}"
            : $"{attackerName} играет Атаковать";

        _actionDescriptions[actionId] = description;
        _actionTimestamps[actionId] = DateTime.UtcNow;
        _sessionActiveAction[sessionId] = actionId;

        // Запоминаем, является ли атакующий текущим игроком
        _isCurrentPlayerAction[actionId] = isCurrentPlayer;

        // Разное время автоочистки
        if (isCurrentPlayer)
        {
            // Для игрока на своем ходу - 30 секунд
            Task.Delay(30000).ContinueWith(_ => CleanupAction(actionId, sessionId));
        }
        else
        {
            // Для других игроков - 10 секунд
            Task.Delay(10000).ContinueWith(_ => CleanupAction(actionId, sessionId));
        }
    }


    // НОВЫЙ МЕТОД: Регистрация действия комбо
    // НОВЫЙ МЕТОД: Регистрация действия комбо
    public static void RegisterComboAction(Guid sessionId, Guid actionId, string playerName, int comboType)
    {
        var description = $"{playerName} играет комбо ({comboType} карты)";

        _actionDescriptions[actionId] = description;
        _actionTimestamps[actionId] = DateTime.UtcNow;
        _sessionActiveAction[sessionId] = actionId;

        // Автоочистка через 10 секунд
        Task.Delay(10000).ContinueWith(_ => CleanupAction(actionId, sessionId));
    }

    // НОВЫЙ МЕТОД: Очистка действия с удалением из сессии
    public static void CleanupAction(Guid actionId, Guid sessionId)
    {
        _actionDescriptions.TryRemove(actionId, out _);
        _actionTimestamps.TryRemove(actionId, out _);
        _actionNopes.TryRemove(actionId, out _);

        // Если это было активное действие для сессии - очищаем
        if (_sessionActiveAction.TryGetValue(sessionId, out var activeId) && activeId == actionId)
        {
            _sessionActiveAction.TryRemove(sessionId, out _);
        }
    }

    // Метод для проверки, было ли действие отменено Нетом
    public static bool IsActionNoped(Guid actionId)
    {
        // Действие отменено, если есть нечетное количество Нетов
        if (_actionNopes.TryGetValue(actionId, out var nopePlayers))
        {
            // Нечетное количество Нетов = действие отменено
            // Четное количество Нетов = действие выполняется (Нет на Нет)
            return nopePlayers.Count % 2 == 1;
        }
        return false;
    }


    // Метод проверки возможности играть Нет
    public static bool CanPlayNopeOnAction(Guid actionId, bool isCurrentPlayer)
    {
        // Если действие уже очищено
        if (!_actionTimestamps.ContainsKey(actionId))
            return false;

        // Если игрок на своем ходу И это его действие - может играть в любое время
        if (isCurrentPlayer &&
            _isCurrentPlayerAction.TryGetValue(actionId, out var isCurrentPlayerAction) &&
            isCurrentPlayerAction)
        {
            return true;
        }

        // Если не на своем ходу - только в течение 5 секунд
        var timeSinceAction = DateTime.UtcNow - _actionTimestamps[actionId];
        return timeSinceAction.TotalSeconds <= 5;
    }

    public static bool IsActionStillActive(Guid actionId)
    {
        return _actionTimestamps.ContainsKey(actionId) &&
               (DateTime.UtcNow - _actionTimestamps[actionId]).TotalSeconds <= 5;
    }

    public static string GetActionDescription(Guid actionId)
    {
        return _actionDescriptions.TryGetValue(actionId, out var description)
            ? description
            : "неизвестное действие";
    }

    public static bool HasPlayerAlreadyNoped(Guid actionId, Player player)
    {
        return _actionNopes.TryGetValue(actionId, out var nopePlayers) &&
               nopePlayers.Any(p => p.Id == player.Id);
    }

    public static void RegisterNopeForAction(Guid actionId, Player player)
    {
        if (!_actionNopes.ContainsKey(actionId))
        {
            _actionNopes[actionId] = new List<Player>();
        }
        _actionNopes[actionId].Add(player);
    }

    public static Guid? GetActiveActionForSession(Guid sessionId)
    {
        // Ищем самое свежее действие для этой сессии
        var latestAction = _actionTimestamps
            .Where(kv => IsActionStillActive(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        return latestAction.Key != Guid.Empty ? latestAction.Key : null;
    }
}