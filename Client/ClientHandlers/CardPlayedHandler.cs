using System.Text;
using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.CardPlayed)]
public class CardPlayedHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var data = Encoding.UTF8.GetString(payload);
        client.AddToLog($"Карта сыграна: {data}");
        return Task.CompletedTask;
    }
}