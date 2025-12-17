using System.Text;
using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.GameOver)]
public class GameOverHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var winnerName = Encoding.UTF8.GetString(payload);

        if (string.IsNullOrEmpty(winnerName))
        {
            client.AddToLog("🏁 Игра окончена без победителя!");
        }
        else if (winnerName == client.PlayerName)
        {
            client.AddToLog("🎉 ПОБЕДА! Вы выиграли игру!");
        }
        else
        {
            client.AddToLog($"🏆 Игра окончена! Победитель: {winnerName}");
        }

        client.SessionId = null;
        client.Hand.Clear();

        return Task.CompletedTask;
    }
}