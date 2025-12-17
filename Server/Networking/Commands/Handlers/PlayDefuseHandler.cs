using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using Server.Networking.Protocol;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayDefuse)]
public class PlayDefuseHandler : ICommandHandler
{
    private class PendingExplosion
    {
        public Player Player { get; set; } = null!;
        public GameSession Session { get; set; } = null!;
        public DateTime Timestamp { get; set; }
        public CancellationTokenSource? TimeoutToken { get; set; }
    }

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

        if (!_pendingExplosions.TryGetValue(player.Id, out var pending) ||
            pending.Session.Id != session.Id ||
            (DateTime.UtcNow - pending.Timestamp).TotalSeconds > 31) 
        {
            await player.Connection.SendMessage("❌ Слишком поздно! Время для обезвреживания истекло.");
            return;
        }

        if (!player.HasDefuseCard)
        {
            await player.Connection.SendMessage("❌ У вас нет карты Обезвредить!");
            await HandlePlayerElimination(session, player, true);
            return;
        }

        if (!int.TryParse(parts[2], out var position) || position < 0)
        {
            position = 0; 
        }

        position = Math.Min(position, 20);

        try
        {
            pending.TimeoutToken?.Cancel();

            var explodingKitten = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);
            if (explodingKitten == null)
            {
                await player.Connection.SendMessage("❌ Взрывной котенок не найден в вашей руке!");
                return;
            }

            var defuseCard = player.RemoveCard(CardType.Defuse);
            if (defuseCard == null)
            {
                await player.Connection.SendMessage("❌ Не удалось найти карту Обезвредить!");
                return;
            }

            player.Hand.Remove(explodingKitten);

            session.GameDeck.Discard(defuseCard);

            session.GameDeck.InsertCard(explodingKitten, position);

            _pendingExplosions.TryRemove(player.Id, out _);

            await session.BroadcastMessage($"✅ {player.Name} обезвредил Взрывного Котенка!");
            await session.BroadcastMessage($"{player.Name} вернул котенка в колоду на позицию {position} от верха.");

            if (session.TurnManager != null)
            {
                session.TurnManager.CardDrawn();
                await session.TurnManager.CompleteTurnAsync();
            }

            await player.Connection.SendPlayerHand(player);

            await SendDefuseSuccessMessage(player, position);

            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при обезвреживании котенка: {ex.Message}");
        }
    }

    private async Task SendDefuseSuccessMessage(Player player, int position)
    {
        var message = $"🎯 Вы успешно обезвредили Взрывного Котенка! " +
                     $"Котенок возвращен в колоду на позицию {position}.";

        var data = KittensPackageBuilder.MessageResponse(message);
        await player.Connection.SendAsync(data, SocketFlags.None);
    }

    public static async void RegisterExplosion(GameSession session, Player player)
    {
        var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pending = new PendingExplosion
        {
            Player = player,
            Session = session,
            Timestamp = DateTime.UtcNow,
            TimeoutToken = timeoutToken
        };

        _pendingExplosions[player.Id] = pending;

        try
        {
            await Task.Delay(30000, timeoutToken.Token);

            if (_pendingExplosions.TryGetValue(player.Id, out var current) &&
                current.Timestamp == pending.Timestamp)
            {
                await HandleTimeoutElimination(session, player);
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static async Task HandleTimeoutElimination(GameSession session, Player player)
    {
        _pendingExplosions.TryRemove(player.Id, out _);

        if (!player.IsAlive) return;

        var eliminationMessage = $"💥 Время вышло! Вы не успели обезвредить котенка и выбываете из игры.";
        var eliminationData = KittensPackageBuilder.MessageResponse(eliminationMessage);
        await player.Connection.SendAsync(eliminationData, SocketFlags.None);

        await BroadcastEliminationMessageToAll(session, player.Name);

        if (player.HasDefuseCard)
        {
            await session.BroadcastMessage($"💥 {player.Name} не успел обезвредить котенка и выбывает из игры!");
        }
        else
        {
            await session.BroadcastMessage($"💥 {player.Name} не имеет карты Обезвредить и выбывает из игры!");
        }

        session.EliminatePlayer(player);
        await session.BroadcastGameState();
    }

    private static async Task BroadcastEliminationMessageToAll(GameSession session, string playerName)
    {
        var message = $"🚫 {playerName} выбыл из игры!";
        var data = KittensPackageBuilder.MessageResponse(message);

        var tasks = new List<Task>();

        foreach (var player in session.Players)
        {
            if (player.Connection != null && player.Connection.Connected)
            {
                tasks.Add(player.Connection.SendAsync(data, SocketFlags.None));
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    public static bool HasPendingExplosion(Player player)
    {
        return _pendingExplosions.ContainsKey(player.Id);
    }

    private async Task HandlePlayerElimination(GameSession session, Player player, bool fromDefuseHandler = false)
    {
        if (_pendingExplosions.TryGetValue(player.Id, out var pending))
        {
            pending.TimeoutToken?.Cancel();
            _pendingExplosions.TryRemove(player.Id, out _);
        }

        if (player.IsAlive)
        {
            var eliminationMessage = "💥 Вы выбываете из игры!";
            var eliminationData = KittensPackageBuilder.MessageResponse(eliminationMessage);
            await player.Connection.SendAsync(eliminationData, SocketFlags.None);

            await BroadcastEliminationMessageToAll(session, player.Name);

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