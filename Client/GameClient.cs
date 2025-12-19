using Client.ClientHandlers;
using Client.Input;
using Client.Models;
using Client.Networking;
using Client.UI;
using Shared.Models;
using System.Net.Sockets;

namespace Client;

public class GameClient
{
    // Свойства состояния
    public Guid? SessionId { get; set; }
    public Guid PlayerId { get; set; }
    public List<Card> Hand { get; } = new();
    public string PlayerName { get; set; } = "Игрок";
    public List<PlayerInfoDto> OtherPlayers { get; } = new();
    public bool Running { get; set; } = true;
    public GameState CurrentGameState { get; set; }
    public List<string> GameLog { get; } = new();

    // Зависимости
    private readonly Socket _socket;
    private readonly KittensClientHelper _helper;
    private readonly ClientCommandHandlerFactory _handlerFactory;
    private readonly GameCommandProcessor _commandProcessor;
    private readonly GameConsoleRenderer _renderer;
    private readonly GameMessageListener _listener;

    // Приватные поля
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;
    private readonly List<byte> _receiveBuffer = new();

    public GameClient(string host, int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(host, port);

        _helper = new KittensClientHelper(_socket);
        _handlerFactory = new ClientCommandHandlerFactory();
        _renderer = new GameConsoleRenderer(this);
        _commandProcessor = new GameCommandProcessor(this, _helper, _renderer);
        _listener = new GameMessageListener(this, _socket, _handlerFactory, _receiveBuffer);

        _renderer.PrintWelcomeMessage(host, port);
    }

    public async Task Start()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("🎭 Введите ваше имя: ");
        Console.ResetColor();

        PlayerName = Console.ReadLine()?.Trim() ?? "Игрок";
        Console.WriteLine();

        // Запрашиваем список игр
        PrintInfo("🔍 Ищу доступные игры...");
        await _helper.SendGetAvailableGames();
        await Task.Delay(500);

        // Запускаем слушатель сообщений
        _listenerTask = Task.Run(() => _listener.ListenForServerMessages(_cts.Token), _cts.Token);

        // Основной цикл
        await GameLoop();
    }

    private async Task GameLoop()
    {
        _renderer.DisplayHelp();

        while (Running && !_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (_socket.Connected)
                {
                    await HandleUserInput();
                }
                else
                {
                    PrintError("Соединение с сервером потеряно.");
                    Running = false;
                }

                await Task.Delay(100, _cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                PrintError($"Ошибка: {ex.Message}");
            }
        }

        await Stop();
    }

    private async Task HandleUserInput()
    {
        var input = ReadLineSafe("🎮 > ", ConsoleColor.Yellow);
        if (string.IsNullOrEmpty(input)) return;

        await _commandProcessor.ProcessCommand(input);
    }

    // Делегированные методы для взаимодействия с UI
    public void DisplayHand() => _renderer.DisplayHand();
    public void DisplayPlayers() => _renderer.DisplayPlayers();
    public void DisplayHelp() => _renderer.DisplayHelp();
    public void DisplayAvailableGames(List<GameInfo> games) => _renderer.DisplayAvailableGames(games);

    // Метод для обновления списка игр в рендерере
    public void UpdateAvailableGames(List<GameInfo> games)
    {
        _renderer.SetAvailableGames(games);
    }

    // Вспомогательные методы
    public void AddToLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        GameLog.Add($"[{timestamp}] {message}");

        if (GameLog.Count > 50)
            GameLog.RemoveAt(0);

        // Определяем цвет сообщения
        ConsoleColor color = ConsoleColor.Gray;
        if (message.Contains("💥") || message.Contains("❌") || message.Contains("Ошибка"))
            color = ConsoleColor.Red;
        else if (message.Contains("✅") || message.Contains("🎉") || message.Contains("ПОБЕДА"))
            color = ConsoleColor.Green;
        else if (message.Contains("🎮") || message.Contains("ВАШ ХОД") || message.Contains("Сейчас ходит"))
            color = ConsoleColor.Yellow;
        else if (message.Contains("💡") || message.Contains("Подсказка"))
            color = ConsoleColor.Cyan;
        else if (message.Contains("⚠️") || message.Contains("Внимание") || message.Contains("таймаут"))
            color = ConsoleColor.DarkYellow;
        else if (message.Contains("Неверный формат") || message.Contains("Не удалось"))
            color = ConsoleColor.Red;

        if (!ShouldFilterMessage(message))
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{timestamp}] {message}");
            Console.ResetColor();
        }
    }

    public void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"💡 {message}");
        Console.ResetColor();
    }

    public void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ {message}");
        Console.ResetColor();
    }

    public async Task<PlayerInfoDto?> SelectPlayerFromList(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n{title}");
        Console.WriteLine("══════════════════════════════════════════");
        Console.ResetColor();

        var alivePlayers = OtherPlayers
            .Where(p => p.IsAlive && p.Id != PlayerId)
            .OrderBy(p => p.Name)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            PrintError("❌ Нет других живых игроков!");
            return null;
        }

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            var player = alivePlayers[i];
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  [{i + 1}] ");
            Console.ResetColor();
            Console.Write($"{player.Name,-15}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Карт: {player.CardCount}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"🎯 Выберите номер игрока (1-{alivePlayers.Count}): ");
        Console.ResetColor();

        var choiceInput = ReadLineSafe();
        if (string.IsNullOrEmpty(choiceInput) || !int.TryParse(choiceInput, out var choice))
        {
            PrintError("❌ Неверный выбор!");
            return null;
        }

        if (choice < 1 || choice > alivePlayers.Count)
        {
            PrintError($"❌ Неверный номер! Выберите от 1 до {alivePlayers.Count}");
            return null;
        }

        var selectedPlayer = alivePlayers[choice - 1];

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Вы выбрали: {selectedPlayer.Name}");
        Console.ResetColor();

        return selectedPlayer;
    }

    public async Task Stop()
    {
        Running = false;
        _cts.Cancel();

        if (_listenerTask != null)
        {
            try { await _listenerTask; }
            catch (OperationCanceledException) { }
        }

        if (_socket.Connected)
        {
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n👋 Клиент остановлен.");
        Console.ResetColor();
    }

    private string? ReadLineSafe(string prompt = "", ConsoleColor promptColor = ConsoleColor.White)
    {
        try
        {
            Console.CursorVisible = true;

            if (!string.IsNullOrEmpty(prompt))
            {
                Console.ForegroundColor = promptColor;
                Console.Write(prompt);
                Console.ResetColor();
            }

            var input = Console.ReadLine();
            Console.CursorVisible = true;

            if (input != null && input.Any(c => c == '\0'))
            {
                input = new string(input.Where(c => c != '\0').ToArray());
            }

            return input?.Trim();
        }
        catch (Exception ex)
        {
            PrintError($"Ошибка чтения ввода: {ex.Message}");
            return null;
        }
    }

    private bool ShouldFilterMessage(string message)
    {
        var filterPatterns = new[]
        {
            "DEBUG",
            "SendCreateGame",
            "SendJoinGame",
            "Package length",
            "Package bytes",
            "Получено байт",
            "Обрабатываем пакет",
            "Пакет успешно разобран",
            "Необработанная команда:",
            "Обновлена рука. Карт:",
            "Игроков в игре:",
            "Карт в колоде:",
            "Ходов сыграно:",
            "DEBUG:",
            "DEBUG Client:",
            "Отправляем FavorResponse:",
            "SendUseCombo:",
            "Ввод:",
            "Разделено на",
            "parts["
        };

        foreach (var pattern in filterPatterns)
        {
            if (message.Contains(pattern))
                return true;
        }

        return false;
    }
}