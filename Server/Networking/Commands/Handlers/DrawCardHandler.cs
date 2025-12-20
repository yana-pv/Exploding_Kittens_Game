using Server.Game.Models;
using Server.Infrastructure;
using Shared.Models;
using Shared.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.DrawCard)]
public class DrawCardHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, byte[]? payload = null, CancellationToken ct = default)
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
            var drawnCard = session.GameDeck.Draw();
            await session.BroadcastMessage($"{player.Name} берет карту из колоды.");

            player.AddToHand(drawnCard);

            session.TurnManager.CardDrawn();

            if (drawnCard.Type == CardType.ExplodingKitten)
            {
                await HandleExplodingKitten(session, player, drawnCard);
            }
            else
            {
                await player.Connection.SendMessage($"Вы взяли: {drawnCard.Name}");
                await player.Connection.SendPlayerHand(player);

                await session.TurnManager.CompleteTurnAsync();

                if (session.State != GameState.GameOver && session.CurrentPlayer != null)
                {
                    await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
                    await session.CurrentPlayer.Connection.SendMessage("Ваш ход! Вы можете сыграть карту или взять карту из колоды.");
                }
            }

            await session.BroadcastGameState();
        }

        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при взятии карты: {ex.Message}");
        }
    }

    private async Task HandleExplodingKitten(GameSession session, Player player, Card kittenCard)
    {
        await session.BroadcastMessage($"💥 {player.Name} вытащил Взрывного Котенка!");

        if (player.HasDefuseCard)
        {
            PlayDefuseHandler.RegisterExplosion(session, player);

            await SendDefuseInstructions(player, session);
        }
        else
        {
            await SendNoDefuseMessage(player);
            await HandlePlayerElimination(session, player, kittenCard);
        }
    }

    private async Task SendDefuseInstructions(Player player, GameSession session)
    {
        var instructions = new[]
        {
        $"💣 ВЗРЫВНОЙ КОТЕНОК! У вас 30 секунд!",
        $"Введите команду: defuse",
        $"⏰ Быстрее! Успейте до конца отсчета!"
    };

        foreach (var message in instructions)
        {
            var data = KittensPackageBuilder.MessageResponse(message);
            await player.Connection.SendAsync(data, SocketFlags.None);
            await Task.Delay(100);
        }
    }

    private async Task SendNoDefuseMessage(Player player)
    {
        var message = "❌ У вас нет карты Обезвредить!\n" +
                     "💥 Вы выбываете из игры!";

        var data = KittensPackageBuilder.MessageResponse(message);
        await player.Connection.SendAsync(data, SocketFlags.None);
    }

    private async Task HandlePlayerElimination(GameSession session, Player player, Card kittenCard)
    {
        session.EliminatePlayer(player);

        await session.TurnManager.CompleteTurnAsync();

        if (session.State != GameState.GameOver && session.CurrentPlayer != null)
        {
            await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
            await session.CurrentPlayer.Connection.SendMessage("Ваш ход!");
        }
    }
}