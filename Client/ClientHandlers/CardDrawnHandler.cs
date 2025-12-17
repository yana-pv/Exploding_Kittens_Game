using Server.Networking.Commands;
using System.Text;

namespace Client.ClientHandlers;

[ClientCommand(Command.CardDrawn)]
public class CardDrawnHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var data = Encoding.UTF8.GetString(payload);
        var parts = data.Split(':');
        
        if (parts.Length >= 2)
        {
            var playerName = parts[0];
            var cardName = parts[1];
            
            if (playerName == client.PlayerName)
            {
                client.AddToLog($"Вы взяли карту: {cardName}");
            }
            else
            {
                client.AddToLog($"{playerName} взял карту: {cardName}");
            }
        }
        
        return Task.CompletedTask;
    }
}