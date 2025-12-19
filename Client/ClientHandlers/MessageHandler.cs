using Client;
using Client.ClientHandlers;
using Shared.Models;
using System.Text;

[ClientCommand(Command.Message)]
public class MessageHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var message = Encoding.UTF8.GetString(payload);
        client.AddToLog(message);

        // Обработка Взрывного Котенка
        if (message.Contains("ВЗРЫВНОЙ КОТЕНОК") ||
            (message.Contains("30 секунд") && message.Contains("defuse")))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n══════════════════════════════════════════");
            Console.WriteLine("⚠️  СРОЧНО! У вас 30 секунд!");
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine("  Введите команду: defuse");
            Console.WriteLine("  ⏰ Быстрее! Успейте до конца отсчета!");
            Console.WriteLine("══════════════════════════════════════════");
            Console.ResetColor();
        }

        return Task.CompletedTask;
    }
}