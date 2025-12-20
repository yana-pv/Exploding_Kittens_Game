using Server.Game.Models;
using Server.Game.Services;
using Server.Infrastructure;
using Server.Networking;
using Server.Networking.Commands;
using Shared.Models;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

[Command(Command.PlayNope)]
public class PlayNopeHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, DateTime> _actionTimestamps = new();
    private static readonly ConcurrentDictionary<Guid, List<Player>> _actionNopes = new();
    private static readonly ConcurrentDictionary<Guid, string> _actionDescriptions = new();
    private static readonly ConcurrentDictionary<Guid, bool> _isCurrentPlayerAction = new();
    private static readonly ConcurrentDictionary<Guid, Guid> _sessionActiveAction = new();
    private static readonly ConcurrentDictionary<Guid, CardType> _actionCardTypes = new();

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

        // Проверяем, есть ли у игрока карта Nope
        if (!player.HasCard(CardType.Nope))
        {
            await sender.SendError(CommandResponse.CardNotFound);
            return;
        }

        // Получаем последнее активное действие в сессии
        var actionId = GetActiveActionForSession(session.Id);
        if (!actionId.HasValue)
        {
            await player.Connection.SendMessage("❌ Нет активных действий для отмены!");
            return;
        }

        if (!CanNopeThisAction(actionId.Value))
        {
            await player.Connection.SendMessage($"❌ Карту 'Нет' нельзя сыграть на это действие!");
            return;
        }

        // Проверяем, не использовал ли уже этот игрок Nope на это действие
        if (_actionNopes.TryGetValue(actionId.Value, out var nopePlayers) &&
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
            if (!_actionNopes.ContainsKey(actionId.Value))
            {
                _actionNopes[actionId.Value] = new List<Player>();
            }
            _actionNopes[actionId.Value].Add(player);

            var description = GetActionDescription(actionId.Value);

            await session.BroadcastMessage($"🚫 {player.Name} сказал НЕТ на: {description}");

            // Проверяем, отменено ли действие
            if (IsActionNoped(actionId.Value))
            {
                await HandleNopeEffect(session, actionId.Value);
            }

            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при игре карты НЕТ: {ex.Message}");
        }
    }

    private static bool CanNopeThisAction(Guid actionId)
    {
        if (!_actionCardTypes.TryGetValue(actionId, out var cardType))
        {
            return true; 
        }

        // НЕЛЬЗЯ отменять:
        switch (cardType)
        {
            case CardType.ExplodingKitten:
            case CardType.Defuse:
                return false;

            // МОЖНО отменять:
            case CardType.Attack:
            case CardType.Skip:
            case CardType.Favor:
            case CardType.Shuffle:
            case CardType.SeeTheFuture:
            case CardType.Nope:
            case CardType.RainbowCat:
            case CardType.BeardCat:
            case CardType.PotatoCat:
            case CardType.WatermelonCat:
            case CardType.TacoCat:
                return true;

            default:
                return true; 
        }
    }

    private static async Task HandleNopeEffect(GameSession session, Guid actionId)
    {
        var description = GetActionDescription(actionId);

        if (description.Contains("атакует") || description.Contains("Атаковать"))
        {
            await session.BroadcastMessage("⚡ Атака отменена картой НЕТ!");

            if (session.TurnManager != null)
            {
                ResetTurnManagerFlagsStatic(session.TurnManager);
            }

            if (session.State == GameState.ResolvingAction)
            {
                session.State = GameState.PlayerTurn;
            }
        }
        else if (description.Contains("комбо"))
        {
            await session.BroadcastMessage("⚡ Комбо отменено картой НЕТ!");
        }
        else if (description.Contains("пропускает") || description.Contains("Пропустить"))
        {
            await session.BroadcastMessage("⚡ Пропуск отменен картой НЕТ!");
        }
        else if (description.Contains("одолжение") || description.Contains("Одолжение"))
        {
            await session.BroadcastMessage("⚡ Одолжение отменено картой НЕТ!");
        }
        else if (description.Contains("перемешал") || description.Contains("Перемешать"))
        {
            await session.BroadcastMessage("⚡ Перемешивание отменено картой НЕТ!");
        }
        else if (description.Contains("заглянул") || description.Contains("Заглянуть"))
        {
            await session.BroadcastMessage("⚡ Заглянуть в будущее отменено картой НЕТ!");
        }
        else
        {
            await session.BroadcastMessage($"⚡ Действие '{description}' отменено картой НЕТ!");
        }

        CompleteAction(actionId, session.Id);
    }

    private static void ResetTurnManagerFlagsStatic(TurnManager turnManager)
    {
        if (turnManager == null) return;

        var turnManagerType = typeof(TurnManager);

        var turnEndedField = turnManagerType.GetField("_turnEnded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (turnEndedField != null)
        {
            turnEndedField.SetValue(turnManager, false);
        }

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

        var hasDrawnCardField = turnManagerType.GetField("_hasDrawnCard",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (hasDrawnCardField != null)
        {
            hasDrawnCardField.SetValue(turnManager, false);
        }
    }

    public static void CompleteAction(Guid actionId, Guid sessionId)
    {
        CleanupAction(actionId, sessionId);
    }

    public static void RegisterAttackAction(Guid sessionId, Guid actionId, string attackerName,
    string? targetName, bool isCurrentPlayer, CardType cardType = CardType.Attack)
    {
        var description = targetName != null
            ? $"{attackerName} атакует {targetName}"
            : $"{attackerName} играет Атаковать";

        _actionDescriptions[actionId] = description;
        _actionTimestamps[actionId] = DateTime.UtcNow;
        _sessionActiveAction[sessionId] = actionId;
        _isCurrentPlayerAction[actionId] = isCurrentPlayer;
        _actionCardTypes[actionId] = cardType; 
    }

    public static void RegisterComboAction(Guid sessionId, Guid actionId, string playerName,
        int comboType, CardType firstCardType)
    {
        var description = $"{playerName} играет комбо ({comboType} карты)";

        _actionDescriptions[actionId] = description;
        _actionTimestamps[actionId] = DateTime.UtcNow;
        _sessionActiveAction[sessionId] = actionId;
        _actionCardTypes[actionId] = firstCardType;
    }

    public static void CleanupAction(Guid actionId, Guid sessionId)
    {
        _actionDescriptions.TryRemove(actionId, out _);
        _actionTimestamps.TryRemove(actionId, out _);
        _actionNopes.TryRemove(actionId, out _);
        _isCurrentPlayerAction.TryRemove(actionId, out _);
        _actionCardTypes.TryRemove(actionId, out _);

        if (_sessionActiveAction.TryGetValue(sessionId, out var activeId) && activeId == actionId)
        {
            _sessionActiveAction.TryRemove(sessionId, out _);
        }
    }

    public static bool IsActionNoped(Guid actionId)
    {
        if (_actionNopes.TryGetValue(actionId, out var nopePlayers))
        {
            return nopePlayers.Count % 2 == 1;
        }
        return false;
    }

    public static bool CanPlayNopeOnAction(Guid actionId, bool isCurrentPlayer)
    {
        return _actionDescriptions.ContainsKey(actionId);
    }

    public static bool IsActionStillActive(Guid actionId)
    {
        return _actionDescriptions.ContainsKey(actionId);
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
        // Получаем самое последнее действие для этой сессии
        if (_sessionActiveAction.TryGetValue(sessionId, out var actionId))
        {
            if (IsActionStillActive(actionId))
            {
                return actionId;
            }
            else
            {
                _sessionActiveAction.TryRemove(sessionId, out _);
            }
        }

        // Если нет активного действия в словаре, ищем последнее по времени
        var latestAction = _actionTimestamps
            .Where(kv => IsActionStillActive(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        return latestAction.Key != Guid.Empty ? latestAction.Key : null;
    }
}