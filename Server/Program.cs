using Server.Infrastructure;
using System.Net;

namespace Server;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Сервер Взрывные Котята ===");

        var ipAddress = IPAddress.Parse("127.0.0.1");
        var port = 5001;

        if (args.Length >= 1)
        {
            if (!IPAddress.TryParse(args[0], out ipAddress!))
            {
                Console.WriteLine($"Неверный IP адрес: {args[0]}, используется 127.0.0.1");
                ipAddress = IPAddress.Parse("127.0.0.1");
            }
        }

        if (args.Length >= 2)
        {
            if (!int.TryParse(args[1], out port))
            {
                Console.WriteLine($"Неверный порт: {args[1]}, используется 5001");
                port = 5001;
            }
        }

        var endPoint = new IPEndPoint(ipAddress, port);
        var server = new EKServer(endPoint);

        try
        {
            await server.StartAsync();
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Фатальная ошибка: {ex.Message}");
        }
    }
}