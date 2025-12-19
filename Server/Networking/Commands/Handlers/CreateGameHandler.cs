using Server.Game.Models;
using Server.Infrastructure; 
using Shared.Models;
using Shared.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.CreateGame)]
public class CreateGameHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, 
        byte[]? payload = null, CancellationToken ct = default)
    {
        var playerName = payload != null && payload.Length > 0
            ? Encoding.UTF8.GetString(payload)
            : $"Player_{new Random().Next(1000)}";

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            GameDeck = new Deck()
        };

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

        sessionManager.CreateSession(session); 

        await sender.SendAsync(KittensPackageBuilder.CreateGameResponse(session.Id, player.Id),
            SocketFlags.None);

        await sender.SendMessage($"Игра создана! ID: {session.Id}");
        await sender.SendMessage($"Вы вошли как: {playerName}");
        await sender.SendMessage($"Ожидание игроков ({session.MinPlayers}-{session.MaxPlayers})...");
    }
}