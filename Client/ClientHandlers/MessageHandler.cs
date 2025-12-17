using Client;
using Client.ClientHandlers;
using Server.Networking.Commands;
using System.Text;

[ClientCommand(Command.Message)]
public class MessageHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var message = Encoding.UTF8.GetString(payload);
        client.AddToLog(message);

        // ДОБАВЛЯЕМ ОСОБУЮ ОБРАБОТКУ ДЛЯ ВЗРЫВНОГО КОТЕНКА
        if (message.Contains("ВЗРЫВНОЙ КОТЕНОК") || message.Contains("defuse"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n══════════════════════════════════════════");
            Console.WriteLine("⚠️  СРОЧНО! У вас 30 секунд на обезвреживание!");
            Console.WriteLine("Используйте: defuse [номер_позиции]");
            Console.WriteLine("Пример: defuse 0 (положить наверх колоды)");
            Console.WriteLine("══════════════════════════════════════════");
            Console.ResetColor();
        }

        // Автоматически извлекаем ID действия из сообщений
        if (message.Contains("nope") && message.Contains("Используйте:"))
        {
            // Пытаемся найти GUID в сообщении
            var guidPattern = @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
            var match = System.Text.RegularExpressions.Regex.Match(message, guidPattern);

            if (match.Success)
            {
                client._lastActiveActionId = Guid.Parse(match.Value);
                Console.WriteLine($"💡 Обнаружено действие для Нета: {client._lastActiveActionId}");
                Console.WriteLine($"💡 Используйте: nope {client._lastActiveActionId}");
            }
        }

        return Task.CompletedTask;
    }
}