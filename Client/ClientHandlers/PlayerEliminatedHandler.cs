using System.Text;
using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.PlayerEliminated)]
public class PlayerEliminatedHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var playerName = Encoding.UTF8.GetString(payload);

        if (playerName == client.PlayerName)
        {
            client.AddToLog("💥 Вы выбыли из игры!");
            client.Hand.Clear();
        }
        else
        {
            client.AddToLog($"💥 {playerName} выбыл из игры!");
        }

        return Task.CompletedTask;
    }
}