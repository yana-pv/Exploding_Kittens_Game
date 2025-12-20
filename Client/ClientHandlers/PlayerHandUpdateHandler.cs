using Client;
using Client.ClientHandlers;
using Shared.Models;
using System.Text;
using System.Text.Json;

[ClientCommand(Command.PlayerHandUpdate)]
public class PlayerHandUpdateHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);

        try
        {
            var dtoCards = JsonSerializer.Deserialize<List<ClientCardDto>>(json);
            if (dtoCards != null)
            {
                var clientSideCards = dtoCards.Select(dto => Card.Create(dto.Type)).ToList();

                client.Hand.Clear();
                client.Hand.AddRange(clientSideCards);
                Console.WriteLine(); 
                client.DisplayHand();
            }
        }
        catch (JsonException ex)
        {
            client.AddToLog($"Ошибка разбора карт: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}