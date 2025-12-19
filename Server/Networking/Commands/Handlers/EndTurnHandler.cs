using Server.Infrastructure;
using Shared.Models;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.EndTurn)]
public class EndTurnHandler : ICommandHandler
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

        if (session.CurrentPlayer != player)
        {
            await sender.SendError(CommandResponse.NotYourTurn);
            return;
        }

        try
        {
            if (session.TurnManager.MustDrawCardBeforeEnd)
            {
                await player.Connection.SendMessage("❌ Вы должны взять карту из колоды перед завершением хода!");
                await player.Connection.SendMessage("Используйте команду: draw");
                await sender.SendError(CommandResponse.InvalidAction);
                return;
            }

            if (session.TurnManager.SkipPlayed || session.TurnManager.AttackPlayed)
            {
                await player.Connection.SendMessage("Ход уже завершен картой Skip/Attack!");
                return;
            }

            if (session.TurnManager.HasDrawnCard)
            {
                session.TurnManager.EndTurn();

                await session.TurnManager.CompleteTurnAsync();

                if (session.State != GameState.GameOver && session.CurrentPlayer != null)
                {
                    await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
                    await session.CurrentPlayer.Connection.SendMessage("Ваш ход! Вы можете:");
                    await session.CurrentPlayer.Connection.SendMessage("1. Сыграть карту (play [номер])");
                    await session.CurrentPlayer.Connection.SendMessage("2. Взять карту из колоды (draw)");
                }
            }
            else
            {
                await player.Connection.SendMessage("❌ Нельзя завершить ход! Вы должны:");
                await player.Connection.SendMessage("1. Взять карту (draw) ИЛИ");
                await player.Connection.SendMessage("2. Сыграть карту Skip/Attack");
            }

            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при завершении хода: {ex.Message}");
        }
    }
}