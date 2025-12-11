using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using Server.Networking.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.JoinGame)]
public class JoinGameHandler : ICommandHandler
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
        var parts = data.Split(':', 2);

        Console.WriteLine($"DEBUG: JoinGameHandler received payload: {BitConverter.ToString(payload)}");
        Console.WriteLine($"DEBUG: Decoded data: '{data}'");
        Console.WriteLine($"DEBUG: Parts after split: [{string.Join(" | ", parts)}]");

        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var gameId))
        {
            Console.WriteLine($"DEBUG: Failed to parse gameId from part: '{parts[0]}' (Parts length: {parts.Length})");
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // --- Обновлённое логирование и получение сессии ---
        Console.WriteLine($"DEBUG: Looking for game ID: {gameId}");
        var activeSessions = sessionManager.GetActiveSessions(); // Получаем сессии через менеджер
        Console.WriteLine($"DEBUG: Current game IDs in sessionManager (via GetActiveSessions): [{string.Join(", ", activeSessions.Select(s => s.Id))}]");

        var session = sessionManager.GetSession(gameId); // <-- Изменено: получаем напрямую из менеджера
        // --- Конец изменений ---

        if (session == null)
        {
            Console.WriteLine($"DEBUG: Game with ID {gameId} was NOT FOUND in sessionManager.");
            await sender.SendError(CommandResponse.GameNotFound);
            return;
        }

        Console.WriteLine($"DEBUG: Game with ID {gameId} was FOUND in sessionManager.");

        if (session.State != GameState.WaitingForPlayers)
        {
            await sender.SendError(CommandResponse.GameAlreadyStarted);
            return;
        }

        if (session.IsFull)
        {
            await sender.SendError(CommandResponse.GameFull);
            return;
        }

        var playerName = parts[1];
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Connection = sender,
            Name = playerName
        };

        if (!session.AddPlayer(player))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        await sender.SendAsync(KittensPackageBuilder.CreateGameResponse(session.Id, player.Id),
            SocketFlags.None);

        await sender.SendMessage($"Вы присоединились к игре как: {playerName}");

        // Уведомляем всех игроков
        await session.BroadcastMessage($"{playerName} присоединился к игре!");
        await session.BroadcastGameState();

        // Если набралось достаточно игроков, предлагаем начать
        if (session.CanStart && session.Players.Count == session.MaxPlayers)
        {
            await session.BroadcastMessage($"Игра заполнена! Готовы начать?");
        }
    }
}