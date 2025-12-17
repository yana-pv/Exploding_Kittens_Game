using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayFavor)]
public class FavorResponseHandler : ICommandHandler
{
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

        if (parts.Length < 3)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (!Guid.TryParse(parts[0], out var gameId))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (!Guid.TryParse(parts[1], out var playerId))
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

        if (session.PendingFavor == null)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            await player.Connection.SendMessage("❌ Нет активного запроса на одолжение!");
            return;
        }

        if (session.PendingFavor.Target != player)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            await player.Connection.SendMessage("❌ Этот запрос не для вас!");
            return;
        }

        if (!int.TryParse(parts[2], out var cardIndex))
        {
            await player.Connection.SendMessage($"❌ Неверный номер карты! Используйте число от 0 до {player.Hand.Count - 1}");
            await player.Connection.SendPlayerHand(player);
            return;
        }

        if (cardIndex < 0 || cardIndex >= player.Hand.Count)
        {
            await player.Connection.SendMessage($"❌ Неверный номер карты! У вас только {player.Hand.Count} карт (0-{player.Hand.Count - 1})");
            await player.Connection.SendPlayerHand(player);
            return;
        }

        var favor = session.PendingFavor;
        var stolenCard = player.Hand[cardIndex];

        player.Hand.RemoveAt(cardIndex);

        favor.Requester.AddToHand(stolenCard);

        await session.BroadcastMessage($"✅ {favor.Requester.Name} взял карту '{stolenCard.Name}' у {player.Name}!");

        session.PendingFavor = null;
        session.State = GameState.PlayerTurn;

        await player.Connection.SendMessage($"📤 Вы отдали карту: {stolenCard.Name}");
        await player.Connection.SendPlayerHand(player);

        await favor.Requester.Connection.SendMessage($"📥 Вы получили карту: {stolenCard.Name}");
        await favor.Requester.Connection.SendPlayerHand(favor.Requester);

        await session.BroadcastGameState();
    }
}