using System.Net.Sockets;

namespace Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "Взрывные Котята - Клиент";
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Console.ResetColor();

        Console.WriteLine("=== ВЗРЫВНЫЕ КОТЯТА ===");
        Console.WriteLine("Сетевой клиент для игры");
        Console.WriteLine();

        string host;
        int port;

        if (args.Length >= 2)
        {
            host = args[0];
            if (!int.TryParse(args[1], out port))
                port = 5001;
        }
        else
        {
            Console.Write("Адрес сервера [127.0.0.1]: ");
            host = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(host))
                host = "127.0.0.1";

            Console.Write("Порт сервера [5001]: ");
            var portInput = Console.ReadLine()?.Trim();
            if (!int.TryParse(portInput, out port))
                port = 5001;
        }

        try
        {
            var client = new GameClient(host, port);

            // Обработка Ctrl+C
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nЗавершение работы...");
                await client.Stop();
            };

            await client.Start();
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Ошибка подключения: {ex.Message}");
            Console.WriteLine("Проверьте, запущен ли сервер и правильность адреса/порта.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Фатальная ошибка: {ex.Message}");
        }

        Console.WriteLine("\nНажмите любую клавишу для выхода...");
        Console.ReadKey();
    }
}