using System.Net.Sockets;

namespace Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "🎮 Взрывные Котята - Клиент";
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.CursorVisible = true;

        PrintWelcomeBanner();

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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("🌐 Адрес сервера [127.0.0.1]: ");
            Console.ResetColor();

            host = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(host))
                host = "127.0.0.1";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("🔢 Порт сервера [5001]: ");
            Console.ResetColor();

            var portInput = Console.ReadLine()?.Trim();
            if (!int.TryParse(portInput, out port))
                port = 5001;
        }

        Console.WriteLine();

        try
        {
            var client = new GameClient(host, port);

            // Обработка Ctrl+C
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n👋 Завершение работы...");
                await client.Stop();
                Environment.Exit(0);
            };

            await client.Start();
        }
        catch (SocketException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Ошибка подключения: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("🔧 Проверьте:");
            Console.WriteLine("   • Запущен ли сервер");
            Console.WriteLine("   • Правильность адреса и порта");
            Console.WriteLine("   • Наличие сетевого подключения");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"💥 Фатальная ошибка: {ex.Message}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n👋 Нажмите любую клавишу для выхода...");
        Console.ResetColor();
        Console.ReadKey();
    }

    private static void PrintWelcomeBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  🐱 ВЗРЫВНЫЕ КОТЯТА 🐱                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }
}