using Server.Game.Enums;
using Server.Infrastructure;
using Server.Networking;
using Server.Networking.Commands;
using Server.Networking.Commands.Handlers;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;


[Command(Command.StealCard)]
public class StealCardHandler : ICommandHandler
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

        // ТЕПЕРЬ ОЖИДАЕМ: gameId:playerId:cardIndex
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

        if (!int.TryParse(parts[2], out var cardIndex))
        {
            await player.Connection.SendMessage("❌ Неверный номер карты!");
            return;
        }

        // Пытаемся завершить кражу (цель берется из PendingStealAction)
        var success = await UseComboHandler.TryCompleteSteal(session, player, cardIndex);

        if (!success)
        {
            await player.Connection.SendMessage("❌ Нет активного запроса на кражу или неверные данные!");
        }
    }
}