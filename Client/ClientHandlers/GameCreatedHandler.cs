using System.Text;
using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.GameCreated)]
public class GameCreatedHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var data = Encoding.UTF8.GetString(payload);
        var parts = data.Split(':');

        if (parts.Length == 2 &&
            Guid.TryParse(parts[0], out var gameId) &&
            Guid.TryParse(parts[1], out var playerId))
        {
            client.SessionId = gameId;
            client.PlayerId = playerId;

            client.AddToLog($"✅ Игра создана! ID: {gameId}");
            client.AddToLog($"Ваш ID игрока: {playerId}");
            client.AddToLog("Сообщите ID игры другим игрокам для присоединения");
        }

        return Task.CompletedTask;
    }
}