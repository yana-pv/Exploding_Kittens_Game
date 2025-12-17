using Server.Game.Enums;
using Server.Infrastructure; 
using Server.Networking.Protocol; 
using System.Net.Sockets;
using System.Text;
using System.Text.Json; 

namespace Server.Networking.Commands.Handlers;

[Command(Command.StartGame)]
public class StartGameHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, 
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (!Guid.TryParse(Encoding.UTF8.GetString(payload), out var gameId))
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

        var player = session.GetPlayerBySocket(sender);
        if (player == null)
        {
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        if (session.State != GameState.WaitingForPlayers)
        {
            await sender.SendError(CommandResponse.GameAlreadyStarted);
            return;
        }

        if (!session.CanStart)
        {
            await sender.SendError(CommandResponse.NotEnoughCards);
            return;
        }

        try
        {
            session.StartGame();

            await session.BroadcastMessage($"Игра началась! Первым ходит {session.CurrentPlayer!.Name}");
            await session.CurrentPlayer!.Connection.SendMessage("Ваш ход! Вы можете сыграть карту или взять карту из колоды.");

            bool handSendSuccess = true;
            foreach (var p in session.Players)
            {
                try
                {
                    var handJson = JsonSerializer.Serialize(p.Hand);
                    var handJsonBytes = Encoding.UTF8.GetBytes(handJson);
                   
                    await p.Connection.SendPlayerHand(p);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки руки игроку {p.Name}: {ex.Message}");
                    handSendSuccess = false;
                    break; 
                }
            }

            if (!handSendSuccess)
            {
                await sender.SendMessage("Ошибка при отправке начальных карт.");
                return; 
            }

            try
            {
                var gameStateJson = session.GetGameStateJson();
                var gameStateJsonBytes = Encoding.UTF8.GetBytes(gameStateJson);
 
                await session.BroadcastGameState(); 
            }
            catch (Exception ex)
            {
                await sender.SendMessage($"Ошибка при отправке состояния игры: {ex.Message}");

                return;
            }
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при запуске игры: {ex.Message}");
        }
    }
}