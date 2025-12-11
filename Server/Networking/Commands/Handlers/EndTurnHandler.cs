using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.EndTurn)]
public class EndTurnHandler : ICommandHandler
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
            // Проверяем, взял ли игрок уже карту в этом ходу
            if (!session.TurnManager.HasDrawnCard)
            {
                // Игрок НЕ взял карту - он ДОЛЖЕН взять
                await player.Connection.SendMessage("❌ Вы должны взять карту из колоды перед завершением хода!");
                await player.Connection.SendMessage("Используйте команду: draw");
                await sender.SendError(CommandResponse.InvalidAction);
                return;
            }

            // Игрок взял карту - можно завершать ход
            session.TurnManager.EndTurn();

            // Переходим к следующему игроку
            session.NextPlayer();

            if (session.State != GameState.GameOver)
            {
                await session.BroadcastMessage($"🎮 Ходит {session.CurrentPlayer!.Name}");
                await session.CurrentPlayer!.Connection.SendMessage("Ваш ход! Вы можете:");
                await session.CurrentPlayer!.Connection.SendMessage("1. Сыграть карту (play [номер])");
                await session.CurrentPlayer!.Connection.SendMessage("2. Взять карту из колоды (draw)");
                await session.CurrentPlayer!.Connection.SendMessage("3. Завершить ход (end) - после взятия карты");
            }

            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при завершении хода: {ex.Message}");
        }
    }
}