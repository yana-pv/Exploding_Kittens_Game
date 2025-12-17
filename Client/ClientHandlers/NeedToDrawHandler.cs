using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.NeedToDraw)]
public class NeedToDrawHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        client.AddToLog("⚠️ Вы должны взять карту из колоды перед завершением хода!");
        client.AddToLog("Используйте команду: draw");
        return Task.CompletedTask;
    }
}
