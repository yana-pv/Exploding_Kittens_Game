using Server.Networking.Commands;
using System.Text;

namespace Client;

[ClientCommand(Command.Message)]
public class MessageHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var message = Encoding.UTF8.GetString(payload);
        client.AddToLog(message);
        return Task.CompletedTask;
    }
}