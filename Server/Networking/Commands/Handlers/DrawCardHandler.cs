using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using Server.Networking.Protocol;
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

            Console.WriteLine($"DEBUG DrawCard: игрок {player.Name} взял карту, ExtraTurns до={player.ExtraTurns}");

            // Регистрируем взятие карты
            session.TurnManager.CardDrawn();

            Console.WriteLine($"DEBUG DrawCard: после CardDrawn(), ExtraTurns после={player.ExtraTurns}");

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
                Console.WriteLine($"DEBUG DrawCard: вызываем CompleteTurnAsync()");
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

        // Отправляем срочное сообщение игроку
        await SendUrgentExplosionMessage(player, session);

        if (player.HasDefuseCard)
        {
            PlayDefuseHandler.RegisterExplosion(session, player);

            // Отправляем подробные инструкции
            await SendDefuseInstructions(player, session);

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
                // Игрок успел использовать Defuse - сообщение будет отправлено из PlayDefuseHandler
                Console.WriteLine($"Игрок {player.Name} обезвредил котенка вовремя");
            }
        }
        else
        {
            // Отправляем сообщение об отсутствии дефуза
            await SendNoDefuseMessage(player);
            await HandlePlayerElimination(session, player, kittenCard);
        }
    }

    // Метод: Инструкции по обезвреживанию
    private async Task SendDefuseInstructions(Player player, GameSession session)
    {
        // Делаем сообщение КОРОЧЕ, чтобы не обрезалось
        var shortInstructions = new[]
        {
        $"💣 ВЗРЫВНОЙ КОТЕНОК! У вас 30 сек!",
        $"ID игры: {session.Id}",
        $"Ваш ID: {player.Id}",
        $"Команда: defuse {session.Id} {player.Id} [0-5]",
        $"Коротко: defuse [позиция] (клиент добавит ID)"
    };

        // Отправляем несколько коротких сообщений
        foreach (var message in shortInstructions)
        {
            var data = KittensPackageBuilder.MessageResponse(message);
            await player.Connection.SendAsync(data, SocketFlags.None);
            await Task.Delay(50);
        }
    }

    // Метод: Срочное сообщение о взрывном котенке
    private async Task SendUrgentExplosionMessage(Player player, GameSession session)
    {
        // СДЕЛАТЬ СООБЩЕНИЕ КОРОЧЕ
        var urgentMessage = player.HasDefuseCard
            ? $"💣 ВЗРЫВНОЙ КОТЕНОК! У вас есть Обезвредить!\nУ вас 30 секунд!"
            : $"💣 ВЗРЫВНОЙ КОТЕНОК! Нет Обезвредить!\n💥 Вы выбываете!";

        var data = KittensPackageBuilder.MessageResponse(urgentMessage);
        await player.Connection.SendAsync(data, SocketFlags.None);
    }

    // Метод: Сообщение об отсутствии дефуза
    private async Task SendNoDefuseMessage(Player player)
    {
        var message = "❌ У вас нет карты Обезвредить!\n" +
                     "💥 Вы выбываете из игры!";

        var data = KittensPackageBuilder.MessageResponse(message);
        await player.Connection.SendAsync(data, SocketFlags.None);
    }

    private async Task HandlePlayerElimination(GameSession session, Player player, Card kittenCard)
    {
        // Игрок выбывает через метод сессии, который теперь отправляет сообщения
        session.EliminatePlayer(player);

        // Переход к следующему игроку через TurnManager
        await session.TurnManager.CompleteTurnAsync();

        if (session.State != GameState.GameOver && session.CurrentPlayer != null)
        {
            await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer.Name}");
            await session.CurrentPlayer.Connection.SendMessage("Ваш ход!");
        }
    }
}