using Client.Models;
using Shared.Models;
using System.Text;

namespace Client.UI;

public class GameConsoleRenderer
{
    private readonly GameClient _client;
    private DateTime _lastDisplayTime = DateTime.MinValue;
    private const int DISPLAY_COOLDOWN_MS = 100;
    private bool _handDisplayed = false;
    private List<GameInfo> _availableGames = new();

    public GameConsoleRenderer(GameClient client)
    {
        _client = client;
        SetupConsole();
    }

    private void SetupConsole()
    {
        Console.Title = "🎮 Взрывные Котята";
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.CursorVisible = true;
    }

    public void SetAvailableGames(List<GameInfo> games)
    {
        _availableGames = games;
    }

    public GameInfo? SelectGameByNumber(int gameNumber)
    {
        var waitingGames = _availableGames
            .Where(g => g.State == GameState.WaitingForPlayers)
            .ToList();

        if (gameNumber < 1 || gameNumber > waitingGames.Count)
        {
            return null;
        }

        return waitingGames[gameNumber - 1];
    }

    public void PrintWelcomeMessage(string host, int port)
    {
        Console.Clear();
        PrintHeader();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Подключено к серверу {host}:{port}");
        Console.ResetColor();
        Console.WriteLine();
    }

    public void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  🐱 ВЗРЫВНЫЕ КОТЯТА 🐱                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    public void DisplayHand()
    {
        if (DateTime.Now - _lastDisplayTime < TimeSpan.FromMilliseconds(DISPLAY_COOLDOWN_MS) && _handDisplayed)
            return;

        _lastDisplayTime = DateTime.Now;
        _handDisplayed = true;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🃏 ВАШИ КАРТЫ 🃏                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        if (_client.Hand.Count == 0)
        {
            Console.WriteLine("   У вас нет карт.");
            return;
        }

        for (int i = 0; i < _client.Hand.Count; i++)
        {
            var card = _client.Hand[i];
            Console.ForegroundColor = GetCardColor(card.Type);
            Console.Write($"   {i,2}.");
            Console.Write($"{card.Name}\n");
        }
        Console.ResetColor();
    }

    public void DisplayPlayers()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      👥 ИГРОКИ 👥                           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        if (_client.OtherPlayers.Count == 0)
        {
            Console.WriteLine("   Нет информации об игроках.");
            Console.WriteLine("   💡 Используйте команду 'state' для получения информации.");
            return;
        }

        var sortedPlayers = _client.OtherPlayers
            .OrderByDescending(p => p.IsCurrentPlayer)
            .ThenBy(p => p.Name)
            .ToList();

        Console.WriteLine("   Статус: ✅ - жив, 💀 - выбыл, 🎮 - сейчас ходит");
        Console.WriteLine();

        foreach (var player in sortedPlayers)
        {
            var statusIcon = player.IsAlive ? "✅" : "💀";
            var currentIcon = player.IsCurrentPlayer ? "🎮" : "  ";

            Console.ForegroundColor = player.IsCurrentPlayer ? ConsoleColor.Yellow :
                                    player.IsAlive ? ConsoleColor.White : ConsoleColor.DarkGray;

            Console.Write($"   {currentIcon} {player.Name,-15} {statusIcon}");
            Console.WriteLine($"  Карт: {player.CardCount,2}");

            if (player.Id == _client.PlayerId)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"        👤 Вы (ID: {player.Id})");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"        ID: {player.Id}");
            }

            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("   💡 Для копирования ID выделите текст и нажмите Ctrl+C");
        Console.ResetColor();
    }

    public void DisplayAvailableGames(List<GameInfo> games)
    {
        _availableGames = games;

        Console.Clear();
        PrintHeader();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                🎮 ДОСТУПНЫЕ ИГРЫ 🎮                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine();

        var waitingGames = games
            .Where(g => g.State == GameState.WaitingForPlayers)
            .ToList();

        if (waitingGames.Count == 0)
        {
            Console.WriteLine("   📭 Нет игр, ожидающих игроков.");
            Console.WriteLine("   💡 Создайте новую игру командой 'create'");
            Console.WriteLine();

            var otherGames = games
                .Where(g => g.State != GameState.WaitingForPlayers)
                .ToList();

            if (otherGames.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("   Другие игры (в процессе):");
                foreach (var game in otherGames)
                {
                    Console.WriteLine($"   • {game.CreatorName} - {game.StateDescription}");
                }
                Console.ResetColor();
            }

            return;
        }

        Console.WriteLine($"   Найдено игр: {waitingGames.Count}");
        Console.WriteLine();

        for (int i = 0; i < waitingGames.Count; i++)
        {
            var game = waitingGames[i];
            DisplayGameInfo(i + 1, game);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n   💡 КАК ПРИСОЕДИНИТЬСЯ:");
        Console.ResetColor();
        Console.WriteLine("      1. Выберите номер игры (1, 2, 3...)");
        Console.WriteLine($"      2. Введите команду: join [номер] [ваше_имя]");
        Console.WriteLine();
        Console.WriteLine($"   💡 Пример для игры #1:");
        Console.WriteLine($"      join 1 {_client.PlayerName}");
        Console.WriteLine();
        Console.WriteLine("   💡 Или создайте новую игру: create");
        Console.ResetColor();
        Console.WriteLine();
    }

    private void DisplayGameInfo(int number, GameInfo game)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"   [{number}] ");
        Console.ResetColor();

        Console.Write($"Создатель: ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{game.CreatorName,-10}");
        Console.ResetColor();

        Console.Write($" | Игроков: ");
        Console.ForegroundColor = game.PlayersCount < game.MaxPlayers ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write($"{game.PlayersCount}/{game.MaxPlayers}");
        Console.ResetColor();

        Console.Write($" | Статус: ");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"{game.StateDescription}");
        Console.ResetColor();

        Console.WriteLine();

        Console.Write($"        Создана: ");
        Console.ForegroundColor = ConsoleColor.DarkGray;

        if (game.TimeSinceCreation.TotalMinutes < 1)
            Console.Write("только что");
        else if (game.TimeSinceCreation.TotalHours < 1)
            Console.Write($"{(int)game.TimeSinceCreation.TotalMinutes} мин назад");
        else
            Console.Write($"{(int)game.TimeSinceCreation.TotalHours} ч назад");

        Console.WriteLine($" | ID: {game.Id}");
        Console.ResetColor();
        Console.WriteLine();
    }

    public void DisplayHelp()
    {
        Console.Clear();
        PrintHeader();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("📚 ОСНОВНЫЕ КОМАНДЫ:");
        Console.ResetColor();
        Console.WriteLine("══════════════════════════════════════════════════════════════════");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("🎮 УПРАВЛЕНИЕ ИГРОЙ:");
        Console.ResetColor();
        Console.WriteLine("  games / list       - Показать доступные игры");
        Console.WriteLine("  create             - Создать новую игровую комнату");
        Console.WriteLine("  join [номер] [имя] - Присоединиться к игре по номеру");
        Console.WriteLine("     Пример: join 1 Иван");
        Console.WriteLine("  join [ID] [имя]    - Присоединиться к игре по ID");
        Console.WriteLine("     Пример: join 550e8400... Иван");
        Console.WriteLine("  start              - Начать игру (только создатель)");
        Console.WriteLine("  hand               - Показать ваши карты");
        Console.WriteLine("  players            - Показать всех игроков и их ID");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("🎴 ИГРОВЫЕ ДЕЙСТВИЯ:");
        Console.ResetColor();
        Console.WriteLine("  play [номер]          - Сыграть карту без цели");
        Console.WriteLine("                         💡 Пример: play 0");
        Console.WriteLine();
        Console.WriteLine("  draw                  - Взять карту из колоды");
        Console.WriteLine("                         ⚠️ Обязательно в конце хода!");
        Console.WriteLine();
        Console.WriteLine("  combo 2 [номера]      - Слепой Карманник (2 одинаковые карты)");
        Console.WriteLine("                         💡 Пример: combo 2 0,1 550e8400...");
        Console.WriteLine("                         📝 Затем: steal [номер_карты_цели]");
        Console.WriteLine();
        Console.WriteLine("  combo 3 [номера]      - Время Рыбачить (3 одинаковые)");
        Console.WriteLine("                         💡 Пример: combo 3 2,3,4 550e8400... Такокот");
        Console.WriteLine("                         📝 Запрашивает конкретную карту у цели");
        Console.WriteLine();
        Console.WriteLine("  combo 5 [номера]      - Воровство из сброса (5 разных карт)");
        Console.WriteLine("                         💡 Пример: combo 5 0,1,2,3,4");
        Console.WriteLine("                         📝 Затем: takediscard [номер_карты_из_сброса]");
        Console.WriteLine();
        Console.WriteLine("  nope [ID_действия]    - Отменить действие картой НЕТ");
        Console.WriteLine("                         💡 Пример: nope 123e4567...");
        Console.WriteLine("                         📝 ID действия показывается при атаке/комбо");
        Console.WriteLine();
        Console.WriteLine("  defuse [позиция]      - Обезвредить Взрывного Котенка");
        Console.WriteLine("                         💡 Пример: defuse 3");
        Console.WriteLine("                         ⏰ 30 секунд на реакцию!");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("🤝 ВЗАИМОДЕЙСТВИЕ С ИГРОКАМИ:");
        Console.ResetColor();
        Console.WriteLine("  give [номер]          - Отдать карту при запросе 'Одолжения'");
        Console.WriteLine("                         💡 Пример: give 2");
        Console.WriteLine("                         ⏰ 30 секунд на выбор!");
        Console.WriteLine();
        Console.WriteLine("  steal [номер]         - Выбрать скрытую карту в 'Слепом Карманнике'");
        Console.WriteLine("                         💡 Пример: steal 1");
        Console.WriteLine("                         ⏰ 30 секунд на выбор!");
        Console.WriteLine();
        Console.WriteLine("  takediscard [номер]   - Взять карту из сброса в комбо 5");
        Console.WriteLine("                         💡 Пример: takediscard 0");
        Console.WriteLine("                         ⏰ 30 секунд на выбор!");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚙️  СИСТЕМНЫЕ КОМАНДЫ:");
        Console.ResetColor();
        Console.WriteLine("  help                  - Показать эту справку");
        Console.WriteLine("  clear                 - Очистить экран");
        Console.WriteLine("  exit / quit           - Выйти из игры");
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("🎯 КАК ИСПОЛЬЗОВАТЬ:");
        Console.ResetColor();
        Console.WriteLine("  1. Посмотрите доступные игры: 'games'");
        Console.WriteLine("  2. Присоединитесь к игре: 'join [номер] [ваше_имя]'");
        Console.WriteLine("  3. Создатель начинает игру: 'start'");
        Console.WriteLine("  4. Используйте 'hand' чтобы видеть карты");
        Console.WriteLine("  5. Используйте 'players' чтобы получить ID других игроков");
        Console.WriteLine("  6. Для атаки или комбо скопируйте ID цели из списка игроков");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("💡 ПРАВИЛА ХОДА:");
        Console.ResetColor();
        Console.WriteLine("  • За ход можно сыграть сколько угодно карт");
        Console.WriteLine("  • В конце хода ОБЯЗАТЕЛЬНО возьмите карту (draw)");
        Console.WriteLine("  • Исключения: карты 'Пропустить' и 'Атаковать'");
        Console.WriteLine("  • Карта 'Атаковать' заставляет следующего игрока ходить дважды");
        Console.WriteLine("  • Карта 'Нет' может отменять другие карты (кроме взрывного котенка)");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("⚠️  ВАЖНО:");
        Console.ResetColor();
        Console.WriteLine("  • При взрывном котенке у вас 30 секунд на 'defuse'");
        Console.WriteLine("  • При 'Одолжении' у цели 30 секунд на 'give'");
        Console.WriteLine("  • При 'Слепом Карманнике' у вас 30 секунд на 'steal'");
        Console.WriteLine("  • Все ID можно скопировать выделением текста и Ctrl+C");
        Console.WriteLine();
    }

    private ConsoleColor GetCardColor(CardType type)
    {
        return type switch
        {
            CardType.ExplodingKitten => ConsoleColor.Red,
            CardType.Defuse => ConsoleColor.Green,
            CardType.Nope => ConsoleColor.Yellow,
            CardType.Attack => ConsoleColor.Magenta,
            CardType.Skip => ConsoleColor.Cyan,
            CardType.Favor => ConsoleColor.Blue,
            CardType.Shuffle => ConsoleColor.DarkGray,
            CardType.SeeTheFuture => ConsoleColor.DarkCyan,
            _ when type >= CardType.RainbowCat && type <= CardType.TacoCat => ConsoleColor.DarkYellow,
            _ => ConsoleColor.White
        };
    }
}