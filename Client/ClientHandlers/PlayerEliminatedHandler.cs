using System.Text;
using Shared.Models;

namespace Client.ClientHandlers;

[ClientCommand(Command.PlayerEliminated)]
public class PlayerEliminatedHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var playerName = Encoding.UTF8.GetString(payload);

        if (playerName == client.PlayerName)
        {
            client.Hand.Clear();
        }
        else
        {
            client.AddToLog($"💥 {playerName} выбыл из игры!");
        }

        return Task.CompletedTask;
    }
}