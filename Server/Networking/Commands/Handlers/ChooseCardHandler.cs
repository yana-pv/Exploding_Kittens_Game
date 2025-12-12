using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.TargetPlayer)] // Используем существующую команду или создаем новую
public class ChooseCardHandler : ICommandHandler
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

        // Проверяем, есть ли pending favor
        if (session.PendingFavor == null || session.PendingFavor.Target != player)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (!int.TryParse(parts[2], out var cardIndex) || cardIndex < 0 || cardIndex >= player.Hand.Count)
        {
            await player.Connection.SendMessage("Неверный номер карты!");
            await player.Connection.SendPlayerHand(player);
            return;
        }

        var favor = session.PendingFavor;
        var stolenCard = player.Hand[cardIndex];

        // Убираем карту у целевого игрока
        player.Hand.RemoveAt(cardIndex);

        // Добавляем карту запрашивающему игроку
        favor.Requester.AddToHand(stolenCard);

        await session.BroadcastMessage($"{favor.Requester.Name} взял карту '{stolenCard.Name}' у {player.Name}!");

        // Очищаем pending действие
        session.PendingFavor = null;
        session.State = GameState.PlayerTurn;

        // Обновляем руки обоих игроков
        await player.Connection.SendPlayerHand(player);
        await favor.Requester.Connection.SendPlayerHand(favor.Requester);
        await session.BroadcastGameState();
    }
}