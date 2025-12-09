namespace Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Запуск сервера 'Взрывные котята'...");

            var server = new GameServer();
            await server.Start();
        }
    }
}