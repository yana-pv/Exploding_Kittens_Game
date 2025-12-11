using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.DrawCard)]
public class DrawCardHandler : ICommandHandler
{
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

        if (session.CurrentPlayer != player)
        {
            await sender.SendError(CommandResponse.NotYourTurn);
            return;
        }

        try
        {
            var drawnCard = session.GameDeck.Draw();
            await session.BroadcastMessage($"{player.Name} берет карту из колоды.");

            // Регистрируем взятие карты
            session.TurnManager.CardDrawn();
            session.NeedsToDrawCard = false;

            if (drawnCard.Type == CardType.ExplodingKitten)
            {
                await HandleExplodingKitten(session, player, drawnCard);
            }
            else
            {
                player.AddToHand(drawnCard);
                await player.Connection.SendMessage($"Вы взяли: {drawnCard.Name}");
                await player.Connection.SendPlayerHand(player);

                // Автоматически завершаем ход после взятия карты
                session.NextPlayer();
                if (session.State != GameState.GameOver)
                {
                    await session.BroadcastMessage($"Ходит {session.CurrentPlayer!.Name}");
                    await session.CurrentPlayer!.Connection.SendMessage("Ваш ход!");
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

        // Если колода пуста, перемешиваем сброс
        if (session.GameDeck.IsEmpty)
        {
            await session.BroadcastMessage("Колода пуста, перемешиваем сброс...");
            // Колода автоматически перемешает сброс при следующем Draw()
        }

        if (player.HasDefuseCard)
        {
            PlayDefuseHandler.RegisterExplosion(player);

            await player.Connection.SendMessage("У вас есть карта Обезвредить! Используйте команду:");
            await player.Connection.SendMessage($"defuse {session.Id} {player.Id} [позиция]");
            await player.Connection.SendMessage("Где [позиция] - куда вернуть котенка в колоду (0 - наверх).");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                await Task.Delay(30000, cts.Token);

                if (PlayDefuseHandler.HasPendingExplosion(player))
                {
                    await HandlePlayerElimination(session, player, kittenCard);
                }
            }
            catch (TaskCanceledException)
            {
                // Игрок успел использовать Defuse
            }
        }
        else
        {
            await HandlePlayerElimination(session, player, kittenCard);
        }
    }

    private async Task HandlePlayerElimination(GameSession session, Player player, Card kittenCard)
    {
        session.EliminatePlayer(player);
        await session.BroadcastMessage($"{player.Name} выбывает из игры!");

        session.NextPlayer();
        if (session.State != GameState.GameOver)
        {
            await session.BroadcastMessage($"Ходит {session.CurrentPlayer!.Name}");
        }
    }
}