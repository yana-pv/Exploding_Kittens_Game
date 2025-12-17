using Server.Networking.Commands;
using System.Text;

namespace Client.ClientHandlers;

[ClientCommand(Command.CardPlayed)]
public class SeeTheFutureHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var data = Encoding.UTF8.GetString(payload);

        // Проверяем, это ли сообщение о "Заглянуть в будущее"
        if (data.Contains("Заглянуть в будущее"))
        {
            // Разбираем сообщение с картами
            var lines = data.Split('\n');
            var futureCards = new List<string>();

            foreach (var line in lines)
            {
                if (line.Contains("1.") || line.Contains("2.") || line.Contains("3."))
                {
                    futureCards.Add(line.Trim());
                }
            }

            if (futureCards.Count > 0)
            {
                client.AddToLog("🔮 Вы видите будущее:");
                foreach (var card in futureCards)
                {
                    client.AddToLog($"  {card}");
                }
            }
        }
        else
        {
            // Обычное сообщение о сыгранной карте
            client.AddToLog($"Карта сыграна: {data}");
        }

        return Task.CompletedTask;
    }
}