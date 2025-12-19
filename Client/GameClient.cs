using Client.ClientHandlers;
using Client.Models;
using Shared.Models;
using Shared.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Client;

public class GameClient
{
    private readonly Socket _socket;
    private readonly KittensClientHelper _helper;
    private readonly ClientCommandHandlerFactory _handlerFactory;

    public Guid? _lastActiveActionId = null;

    public Guid? SessionId { get; set; }
    public Guid PlayerId { get; set; }
    public List<Card> Hand { get; } = new();
    public GameState CurrentGameState { get; set; }
    public bool Running { get; set; } = true;
    public string PlayerName { get; set; } = "Игрок";
    public List<string> GameLog { get; } = new();
    public List<PlayerInfoDto> OtherPlayers { get; } = new();
    private readonly List<byte> _receiveBuffer = new();

    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;

    // Переменные для управления отображением
    private DateTime _lastDisplayTime = DateTime.MinValue;
    private const int DISPLAY_COOLDOWN_MS = 100;
    private bool _handDisplayed = false;
    private string _lastGameState = "";
    private int _consoleWidth = Console.WindowWidth;
    private int _consoleHeight = Console.WindowHeight;

    public GameClient(string host, int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(host, port);
        _helper = new KittensClientHelper(_socket);
        _handlerFactory = new ClientCommandHandlerFactory();

        SetupConsole();
        PrintWelcomeMessage(host, port);
    }

    private void SetupConsole()
    {
        Console.Title = "🎮 Взрывные Котята";
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.CursorVisible = true;

        try
        {
            _consoleWidth = Math.Max(80, Console.WindowWidth);
            _consoleHeight = Math.Max(25, Console.WindowHeight);
        }
        catch
        {
            _consoleWidth = 80;
            _consoleHeight = 25;
        }
    }

    private void PrintWelcomeMessage(string host, int port)
    {
        Console.Clear();
        PrintHeader();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Подключено к серверу {host}:{port}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  🐱 ВЗРЫВНЫЕ КОТЯТА 🐱                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    public async Task Start()
    {
        // Запрашиваем имя игрока
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("🎭 Введите ваше имя: ");
        Console.ResetColor();

        PlayerName = Console.ReadLine()?.Trim() ?? "Игрок";
        Console.WriteLine();

        // Автоматически запрашиваем список игр при подключении
        PrintInfo("🔍 Ищу доступные игры...");
        await _helper.SendGetAvailableGames();

        // Ждем немного для получения ответа
        await Task.Delay(500);

        // Запускаем поток прослушивания
        _listenerTask = Task.Run(ListenForServerMessages, _cts.Token);

        // Основной игровой цикл
        await GameLoop();
    }

    private async Task GameLoop()
    {
        DisplayHelp();

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
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                PrintError($"Ошибка: {ex.Message}");
            }
        }

        await Stop();
    }

    // В Client/GameClient.cs
    public void DisplayAvailableGames(List<GameInfo> games)
    {
        Console.Clear();
        PrintHeader();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                🎮 ДОСТУПНЫЕ ИГРЫ 🎮                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine();

        // Фильтруем только игры, которые ожидают игроков
        var waitingGames = games
            .Where(g => g.State == GameState.WaitingForPlayers)
            .ToList();

        if (waitingGames.Count == 0)
        {
            Console.WriteLine("   📭 Нет игр, ожидающих игроков.");
            Console.WriteLine("   💡 Создайте новую игру командой 'create [имя]'");
            Console.WriteLine();

            // Показываем другие игры для информации
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

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"   [{i + 1}] ");
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

            Console.Write($" | ID: ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine(game.Id.ToString()[..8] + "..."); // Показываем только первые 8 символов
            Console.ResetColor();

            Console.Write($"        Создана: ");
            Console.ForegroundColor = ConsoleColor.DarkGray;

            if (game.TimeSinceCreation.TotalMinutes < 1)
                Console.Write("только что");
            else if (game.TimeSinceCreation.TotalHours < 1)
                Console.Write($"{(int)game.TimeSinceCreation.TotalMinutes} мин назад");
            else
                Console.Write($"{(int)game.TimeSinceCreation.TotalHours} ч назад");

            Console.WriteLine($" | Полный ID: {game.Id}");
            Console.ResetColor();
            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("   💡 КАК ПРИСОЕДИНИТЬСЯ:");
        Console.ResetColor();
        Console.WriteLine("      1. Выберите номер игры (1, 2, 3...)");
        Console.WriteLine($"      2. Скопируйте ID игры (выделите и Ctrl+C)");
        Console.WriteLine($"      3. Введите команду: join [ID] {PlayerName}");
        Console.WriteLine();
        Console.WriteLine($"   💡 Пример для игры #1:");
        Console.WriteLine($"      join {waitingGames[0].Id} {PlayerName}");
        Console.WriteLine();
        Console.WriteLine("   💡 Или создайте новую игру: create [ваше_имя]");
        Console.ResetColor();
        Console.WriteLine();
    }

    private async Task HandleUserInput()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n🎮 > ");
        Console.ResetColor();

        var input = ReadLineSafe();
        if (string.IsNullOrEmpty(input)) return;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var command = parts[0].ToLower();

        try
        {
            switch (command)
            {
                case "create":
                    await HandleCreateCommand(parts);
                    break;

                case "games":
                case "list":
                    await _helper.SendGetAvailableGames();
                    PrintInfo("🔍 Обновляю список игр...");
                    break;

                case "join":
                    await HandleJoinCommand(parts);
                    break;

                case "start":
                    await HandleStartCommand(parts);
                    break;

                case "play":
                    await HandlePlayCommand(parts);
                    break;

                case "draw":
                    await HandleDrawCommand(parts);
                    break;

                case "combo":
                    await HandleComboCommand(parts);
                    break;

                case "nope":
                    await HandleNopeCommand(parts);
                    break;

                case "defuse":
                    await HandleDefuseCommand(parts);
                    break;

                case "hand":
                    DisplayHand();
                    break;

                case "state":
                    if (SessionId.HasValue)
                        await _helper.SendGetGameState(SessionId.Value);
                    break;

                case "players":
                    DisplayPlayers();
                    break;

                case "help":
                    DisplayHelp();
                    break;

                case "favor":
                    await HandleFavorCommand(parts);
                    break;

                case "give":
                    await HandleGiveCommand(parts);
                    break;

                case "steal":
                    await HandleStealCommand(parts);
                    break;

                case "takediscard":
                    await HandleTakeDiscardCommand(parts);
                    break;

                case "end":
                    await HandleEndTurnCommand(parts);
                    break;

                case "clear":
                    Console.Clear();
                    PrintHeader();
                    break;

                case "exit":
                case "quit":
                    Running = false;
                    break;

                default:
                    PrintError($"Неизвестная команда: {command}");
                    Console.WriteLine("💡 Введите 'help' для списка команд");
                    break;
            }
        }
        catch (Exception ex)
        {
            PrintError($"Ошибка выполнения команды: {ex.Message}");
        }
    }

    private async Task HandleCreateCommand(string[] parts)
    {
        var name = PlayerName;
        if (string.IsNullOrEmpty(name))
        {
            PrintError("Имя игрока не может быть пустым!");
            return;
        }

        PrintInfo($"Создаю игру как {name}...");
        await _helper.SendCreateGame(name);
    }

    private async Task HandleJoinCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("📝 Использование: join [ID_игры] [имя]");
            Console.WriteLine("💡 Пример: join 550e8400-e29b-41d4-a716-446655440000 Иван");
            return;
        }

        if (!Guid.TryParse(parts[1], out var gameId))
        {
            PrintError("Неверный формат ID игры");
            return;
        }

        var name = parts.Length > 2 ? parts[2] : PlayerName;

        PrintInfo($"Присоединяюсь к игре {gameId} как {name}...");
        await _helper.SendJoinGame(gameId, name);
    }

    private async Task HandleStartCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре. Сначала создайте или присоединитесь к игре.");
            return;
        }

        PrintInfo("Запуск игры...");
        await _helper.SendStartGame(SessionId.Value);
    }

    private async Task HandlePlayCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("📝 Использование: play [номер_карты]");
            Console.WriteLine("💡 Для карты 'Одолжение' просто введите номер карты, затем выберите игрока");
            DisplayHand(); // Показываем руку при неверном вводе
            return;
        }

        if (cardIndex < 0 || cardIndex >= Hand.Count)
        {
            PrintError($"Неверный номер карты. Доступны номера 0-{Hand.Count - 1}");
            DisplayHand(); // Показываем руку при неверном вводе
            return;
        }

        var card = Hand[cardIndex];

        // Особый случай: карта "Одолжение" (Favor)
        if (card.Type == CardType.Favor)
        {
            await HandleFavorCard(cardIndex);
            return;
        }

        string? targetPlayerId = parts.Length > 2 ? parts[2] : null;

        if (targetPlayerId != null && !Guid.TryParse(targetPlayerId, out _))
        {
            PrintError("ID целевого игрока должен быть в формате GUID!");
            Console.WriteLine("💡 Пример: 550e8400-e29b-41d4-a716-446655440000");
            DisplayHand(); // Показываем руку при ошибке
            return;
        }

        PrintInfo($"Играю карту: {card.Name}");
        await _helper.SendPlayCard(SessionId.Value, PlayerId, cardIndex, targetPlayerId);

        // Ждем немного для получения ответа от сервера
        await Task.Delay(300);

        // ПОКАЗЫВАЕМ РУКУ ПОСЛЕ ИГРЫ КАРТЫ
        DisplayHand();
    }

    private async Task HandleFavorCard(int cardIndex)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🎭 ОДОЛЖЕНИЕ 🎭                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        // Показываем список игроков для выбора
        var selectedPlayer = await SelectPlayerFromList("🎯 Выберите игрока, у которого хотите попросить карту:");
        if (selectedPlayer == null) return;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Вы выбрали: {selectedPlayer.Name}");
        Console.WriteLine($"📤 Играем 'Одолжение' на игрока {selectedPlayer.Name}");
        Console.ResetColor();

        // Подтверждение
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n💡 Подтвердите действие (Enter - да, n - нет): ");
        Console.ResetColor();

        var confirmation = ReadLineSafe();
        if (!string.IsNullOrEmpty(confirmation) && confirmation.ToLower() == "n")
        {
            PrintInfo("❌ Действие отменено.");
            return;
        }

        // Ждем немного для лучшего UX
        await Task.Delay(500);

        // Отправляем команду на сервер
        PrintInfo($"Играю карту 'Одолжение' на {selectedPlayer.Name}");
        await _helper.SendPlayCard(SessionId.Value, PlayerId, cardIndex, selectedPlayer.Id.ToString());

        // Ждем немного для получения ответа от сервера
        await Task.Delay(300);

        // ПОКАЗЫВАЕМ РУКУ ПОСЛЕ ОДОЛЖЕНИЯ
        DisplayHand();
    }

    private async Task HandleDrawCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        PrintInfo("Беру карту из колоды...");
        await _helper.SendDrawCard(SessionId.Value, PlayerId);

        // Ждем немного для получения ответа от сервера
        await Task.Delay(300);

        // ПОКАЗЫВАЕМ РУКУ ПОСЛЕ ВЗЯТИЯ КАРТЫ
        DisplayHand();
    }


    private async Task HandleComboCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 3)
        {
            Console.WriteLine("📝 Использование: combo [тип] [номера_карт через запятую]");
            Console.WriteLine("💡 Примеры:");
            Console.WriteLine("  combo 2 0,1");
            Console.WriteLine("  combo 3 0,1,2");
            Console.WriteLine("  combo 5 0,1,2,3,4");
            DisplayHand(); // Показываем руку при неверном вводе
            return;
        }

        if (!int.TryParse(parts[1], out var comboType) || (comboType != 2 && comboType != 3 && comboType != 5))
        {
            PrintError("❌ Неверный тип комбо. Допустимо: 2, 3, 5");
            return;
        }

        // Парсим индексы карт
        var cardIndices = parts[2].Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => int.TryParse(s, out var i) ? i : -1)
            .Where(i => i >= 0 && i < Hand.Count)
            .Distinct()
            .ToList();

        if (cardIndices.Count != comboType)
        {
            PrintError($"❌ Для комбо типа {comboType} нужно {comboType} разных карт");
            Console.WriteLine($"   Указано: {cardIndices.Count} карт");
            return;
        }

        // Проверяем, что карты подходят для комбо
        var comboCards = cardIndices.Select(i => Hand[i]).ToList();
        if (!ValidateComboCards(comboType, comboCards))
        {
            PrintError($"❌ Выбранные карты не подходят для комбо {comboType}");
            DisplayComboRules(comboType);
            return;
        }

        var cardNames = comboCards.Select(c => c.Name);
        PrintInfo($"🎭 Играю комбо {comboType} с картами: {string.Join(", ", cardNames)}");

        string? targetData = null;

        switch (comboType)
        {
            case 2:
                // Для комбо 2 нужен ID цели
                if (parts.Length > 3)
                {
                    targetData = parts[3];
                }
                else
                {
                    // Показываем список игроков для выбора
                    var selectedTarget = await SelectPlayerFromList("🎯 Выберите цель для Слепого Карманника:");
                    if (selectedTarget == null) return;

                    targetData = selectedTarget.Id.ToString();
                }
                break;

            case 3:
                // Для комбо 3 нужен ID цели и название карты
                if (parts.Length > 4) // combo 3 0,1,2 [номер_игрока] [название_карты]
                {
                    var playerNumber = parts[3];
                    var cardName = parts[4];

                    // Если указан номер игрока, а не ID
                    if (int.TryParse(playerNumber, out var playerIndex))
                    {
                        var alivePlayers = OtherPlayers
                            .Where(p => p.IsAlive && p.Id != PlayerId)
                            .OrderBy(p => p.Name)
                            .ToList();

                        if (playerIndex > 0 && playerIndex <= alivePlayers.Count)
                        {
                            targetData = $"{alivePlayers[playerIndex - 1].Id}|{cardName}";
                        }
                        else
                        {
                            PrintError($"❌ Неверный номер игрока! Доступно: 1-{alivePlayers.Count}");
                            return;
                        }
                    }
                    else
                    {
                        targetData = $"{playerNumber}|{cardName}";
                    }
                }
                else if (parts.Length > 3 && Guid.TryParse(parts[3], out _))
                {
                    // Если указан GUID цели и нет названия карты
                    PrintError("❌ Для комбо 3 укажите также название карты!");
                    Console.WriteLine("💡 Пример: combo 3 0,1,2 [номер_игрока] [название_карты]");
                    return;
                }
                else
                {
                    // Показываем меню выбора игрока и карты
                    await HandleCombo3WithMenu(cardIndices);
                    return;
                }
                break;

            case 5:
                // Для комбо 5 нет целевых данных
                break;
        }

        try
        {
            // Отправляем индексы карт
            var indicesStr = string.Join(",", cardIndices);

            // Используем существующий метод SendUseCombo
            await _helper.SendUseCombo(SessionId.Value, PlayerId, comboType, cardIndices, targetData);

            PrintInfo($"✅ Команда комбо отправлена!");

            // Ждем немного для получения ответа от сервера
            await Task.Delay(500);

            // ПОКАЗЫВАЕМ РУКУ ПОСЛЕ КОМБО
            DisplayHand();
        }
        catch (Exception ex)
        {
            PrintError($"❌ Ошибка отправки комбо: {ex.Message}");
            DisplayHand(); // Показываем руку при ошибке
        }
    }

    private async Task<PlayerInfoDto?> SelectPlayerFromList(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n{title}");
        Console.WriteLine("══════════════════════════════════════════");
        Console.ResetColor();

        // Получаем список живых игроков (кроме себя)
        var alivePlayers = OtherPlayers
            .Where(p => p.IsAlive && p.Id != PlayerId)
            .OrderBy(p => p.Name)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            PrintError("❌ Нет других живых игроков!");
            return null;
        }

        // Показываем список с номерами
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

    private async Task HandleCombo3WithMenu(List<int> cardIndices)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🎣 КОМБО 3: ВРЕМЯ РЫБАЧИТЬ 🎣           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        // 1. Выбор игрока
        var selectedPlayer = await SelectPlayerFromList("🎯 Выберите игрока, у которого хотите взять карту:");
        if (selectedPlayer == null) return;

        // 2. Выбор карты
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n📋 ВЫБЕРИТЕ КАРТУ:");
        Console.WriteLine("══════════════════════════════════════════");
        Console.ResetColor();

        Console.WriteLine("  1. Взрывной Котенок");
        Console.WriteLine("  2. Обезвредить");
        Console.WriteLine("  3. Нет");
        Console.WriteLine("  4. Атаковать");
        Console.WriteLine("  5. Пропустить");
        Console.WriteLine("  6. Одолжение");
        Console.WriteLine("  7. Перемешать");
        Console.WriteLine("  8. Заглянуть в будущее");
        Console.WriteLine("  9. Радужный Кот");
        Console.WriteLine(" 10. Котобородач");
        Console.WriteLine(" 11. Кошка-Картошка");
        Console.WriteLine(" 12. Арбузный Котэ");
        Console.WriteLine(" 13. Такокот");
        Console.WriteLine("══════════════════════════════════════════");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\n🎯 Выберите номер карты (1-13): ");
        Console.ResetColor();

        var cardNumberInput = ReadLineSafe();
        if (!int.TryParse(cardNumberInput, out var cardNumber) || cardNumber < 1 || cardNumber > 13)
        {
            PrintError("❌ Неверный номер карты. Введите число от 1 до 13");
            return;
        }

        // Сопоставляем номер с названием карты
        string cardName = cardNumber switch
        {
            1 => "Взрывной Котенок",
            2 => "Обезвредить",
            3 => "Нет",
            4 => "Атаковать",
            5 => "Пропустить",
            6 => "Одолжение",
            7 => "Перемешать",
            8 => "Заглянуть в будущее",
            9 => "Радужный Кот",
            10 => "Котобородач",
            11 => "Кошка-Картошка",
            12 => "Арбузный Котэ",
            13 => "Такокот",
            _ => "Обезвредить"
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Вы выбрали карту: {cardName}");
        Console.WriteLine($"📤 Запрашиваем карту '{cardName}' у игрока {selectedPlayer.Name}");
        Console.ResetColor();

        // 3. Подтверждение
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n💡 Подтвердите действие (Enter - да, n - нет): ");
        Console.ResetColor();

        var confirmation = ReadLineSafe();
        if (!string.IsNullOrEmpty(confirmation) && confirmation.ToLower() == "n")
        {
            PrintInfo("❌ Действие отменено.");
            return;
        }

        // 4. Отправка команды
        var targetData = $"{selectedPlayer.Id}|{cardName}";
        var indicesStr = string.Join(",", cardIndices);

        try
        {
            await _helper.SendUseCombo(SessionId.Value, PlayerId, 3, cardIndices, targetData);
            PrintInfo($"✅ Команда комбо 3 отправлена!");
        }
        catch (Exception ex)
        {
            PrintError($"❌ Ошибка отправки комбо: {ex.Message}");
        }
    }

    private async Task DisplayCardSelectionForCombo3(PlayerInfoDto targetPlayer)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🎣 ВЫБОР КАРТЫ 🎣                       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"🎯 Цель: {targetPlayer.Name}");
        Console.WriteLine();

        Console.WriteLine("📋 Выберите тип карты, которую хотите запросить:");
        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine("  1. Взрывной Котенок");
        Console.WriteLine("  2. Обезвредить");
        Console.WriteLine("  3. Нет");
        Console.WriteLine("  4. Атаковать");
        Console.WriteLine("  5. Пропустить");
        Console.WriteLine("  6. Одолжение");
        Console.WriteLine("  7. Перемешать");
        Console.WriteLine("  8. Заглянуть в будущее");
        Console.WriteLine("  9. Радужный Кот");
        Console.WriteLine(" 10. Котобородач");
        Console.WriteLine(" 11. Кошка-Картошка");
        Console.WriteLine(" 12. Арбузный Котэ");
        Console.WriteLine(" 13. Такокот");
        Console.WriteLine("══════════════════════════════════════════");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\n🎯 Выберите номер карты (1-13): ");
        Console.ResetColor();

        var cardNumberInput = ReadLineSafe();
        if (!int.TryParse(cardNumberInput, out var cardNumber) || cardNumber < 1 || cardNumber > 13)
        {
            PrintError("❌ Неверный номер карты. Введите число от 1 до 13");
            return;
        }

        // Сопоставляем номер с названием карты
        string cardName = cardNumber switch
        {
            1 => "Взрывной Котенок",
            2 => "Обезвредить",
            3 => "Нет",
            4 => "Атаковать",
            5 => "Пропустить",
            6 => "Одолжение",
            7 => "Перемешать",
            8 => "Заглянуть в будущее",
            9 => "Радужный Кот",
            10 => "Котобородач",
            11 => "Кошка-Картошка",
            12 => "Арбузный Котэ",
            13 => "Такокот",
            _ => "Обезвредить"
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Выбрана карта: {cardName}");
        Console.WriteLine($"📤 Запрашиваем карту '{cardName}' у игрока {targetPlayer.Name}");
        Console.ResetColor();

        // Ждем немного для лучшего UX
        await Task.Delay(500);

        // Получаем текущие индексы карт из контекста (нужно будет сохранять их)
        // Для простоты переделаем логику - пользователь должен ввести полную команду
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n💡 Введите полную команду:");
        Console.WriteLine($"   combo 3 [номера_карт] {targetPlayer.Id} {cardName}");
        Console.ResetColor();
    }

    private async Task HandleNopeCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        // НОВЫЙ ФОРМАТ: просто "nope" без параметров
        if (parts.Length == 1)
        {
            PrintInfo("🚫 Играю карту НЕТ на последнее действие...");
            await _helper.SendPlayNope(SessionId.Value, PlayerId, Guid.Empty); // Или можно не отправлять третий параметр
        }
        else
        {
            // Старый формат для обратной совместимости
            if (Guid.TryParse(parts[1], out var actionId))
            {
                PrintInfo($"🚫 Играю НЕТ на действие {actionId}");
                await _helper.SendPlayNope(SessionId.Value, PlayerId, actionId);
            }
            else
            {
                PrintError("Неверный формат команды!");
                Console.WriteLine("💡 Используйте просто: nope");
            }
        }
    }

    private async Task HandleDefuseCommand(string[] parts)
    {
        if (!SessionId.HasValue || PlayerId == Guid.Empty)
        {
            PrintError("Вы не в игре.");
            return;
        }

        // Проверяем, есть ли карта "Обезвредить" в руке
        var hasDefuseCard = Hand.Any(c => c.Type == CardType.Defuse);
        if (!hasDefuseCard)
        {
            PrintError("❌ У вас нет карты 'Обезвредить' в руке!");
            DisplayHand(); // Показываем руку при ошибке
            return;
        }

        // Команда: defuse (без параметров)
        if (parts.Length == 1)
        {
            PrintInfo("💣 Обезвреживаю котенка...");
            await _helper.SendPlayDefuse(SessionId.Value, PlayerId);

            // Ждем немного для получения ответа от сервера
            await Task.Delay(300);

            // ПОКАЗЫВАЕМ РУКУ ПОСЛЕ ОБЕЗВРЕЖИВАНИЯ
            DisplayHand();
        }
        else
        {
            PrintError("❌ Неверная команда!");
            Console.WriteLine("💡 Используйте просто: defuse");
            DisplayHand(); // Показываем руку при ошибке
        }
    }

    private async Task ListenForServerMessages()
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (Running && !_cts.Token.IsCancellationRequested && _socket.Connected)
            {
                var bytesRead = await _socket.ReceiveAsync(buffer, SocketFlags.None, _cts.Token);
                if (bytesRead == 0) break;

                var data = new byte[bytesRead];
                Array.Copy(buffer, 0, data, 0, bytesRead);

                await ProcessServerMessage(data);
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемое при остановке
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        {
            PrintError("Соединение с сервером разорвано.");
            Running = false;
        }
        catch (Exception ex)
        {
            PrintError($"Ошибка приема данных: {ex.Message}");
            Running = false;
        }
    }

    private async Task ProcessServerMessage(byte[] data)
    {
        // Добавляем данные в буфер без отладочного вывода
        _receiveBuffer.AddRange(data);

        while (_receiveBuffer.Count >= 5)
        {
            int startIndex = -1;
            for (int i = 0; i <= _receiveBuffer.Count - 5; i++)
            {
                if (_receiveBuffer[i] == 0x02)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == -1)
            {
                _receiveBuffer.Clear();
                break;
            }

            if (startIndex > 0)
            {
                _receiveBuffer.RemoveRange(0, startIndex);
                continue;
            }

            var command = _receiveBuffer[1];
            ushort payloadLength = (ushort)(_receiveBuffer[2] | (_receiveBuffer[3] << 8));
            var expectedTotalLength = 1 + 1 + KittensPackageMeta.LengthSize + payloadLength + 1;

            if (_receiveBuffer.Count >= expectedTotalLength)
            {
                var endIndex = expectedTotalLength - 1;
                if (endIndex >= _receiveBuffer.Count || _receiveBuffer[endIndex] != 0x03)
                {
                    _receiveBuffer.RemoveAt(0);
                    continue;
                }

                var packet = _receiveBuffer.Take(expectedTotalLength).ToArray();
                _receiveBuffer.RemoveRange(0, expectedTotalLength);

                var parsed = KittensPackageParser.TryParse(packet, out var error);
                if (parsed != null)
                {
                    var (cmd, payload) = parsed.Value;
                    try
                    {
                        var handler = _handlerFactory.GetHandler(cmd);
                        await handler.Handle(this, payload);
                    }
                    catch (KeyNotFoundException)
                    {
                        await HandleCommandFallback(cmd, payload);
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    public void UpdatePlayersList(List<PlayerInfoDto> players)
    {
        OtherPlayers.Clear();
        OtherPlayers.AddRange(players);

        OtherPlayers.Add(new PlayerInfoDto
        {
            Id = PlayerId,
            Name = PlayerName,
            CardCount = Hand.Count,
            IsAlive = true
        });
    }

    private async Task HandleCommandFallback(Command command, byte[] payload)
    {
        switch (command)
        {
            case Command.Message:
                var message = Encoding.UTF8.GetString(payload);
                AddToLog(message);
                break;

            case Command.Error:
                if (payload.Length > 0)
                {
                    var error = (CommandResponse)payload[0];
                    AddToLog($"❌ Ошибка: {GetErrorMessage(error)}");
                }
                break;

            default:
                // Не показываем технические сообщения пользователю
                break;
        }

        await Task.CompletedTask;
    }

    private string GetErrorMessage(CommandResponse error)
    {
        return error switch
        {
            CommandResponse.GameNotFound => "Игра не найдена",
            CommandResponse.PlayerNotFound => "Игрок не найден",
            CommandResponse.NotYourTurn => "Не ваш ход",
            CommandResponse.InvalidAction => "Недопустимое действие",
            CommandResponse.GameFull => "Игра заполнена",
            CommandResponse.GameAlreadyStarted => "Игра уже началась",
            CommandResponse.CardNotFound => "Карта не найдена",
            CommandResponse.NotEnoughCards => "Недостаточно карт",
            CommandResponse.PlayerNotAlive => "Игрок выбыл",
            CommandResponse.SessionNotFound => "Сессия не найдена",
            CommandResponse.Unauthorized => "Неавторизованный доступ",
            _ => $"Ошибка: {error}"
        };
    }

    private void DisplayHelp()
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
        Console.WriteLine("  create                - Создать новую игровую комнату");
        Console.WriteLine("  join [ID]             - Присоединиться к существующей игре");
        Console.WriteLine("  start                 - Начать игру (только создатель)");
        Console.WriteLine("  hand                  - Показать ваши карты");
        Console.WriteLine("  players               - Показать всех игроков и их ID");
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
        Console.WriteLine("  1. Создайте игру: 'create ВашеИмя'");
        Console.WriteLine("  2. Сообщите ID другим игрокам");
        Console.WriteLine("  3. Начните игру: 'start' (когда все присоединятся)");
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

        if (Hand.Count == 0)
        {
            Console.WriteLine("   У вас нет карт.");
            return;
        }

        // Отображаем все карты в одной колонке с полными названиями
        for (int i = 0; i < Hand.Count; i++)
        {
            var card = Hand[i];
            Console.ForegroundColor = GetCardColor(card.Type);
            Console.Write($"   {i,2}.");

            // Выделяем название карты жирным
            Console.Write($"{card.Name}\n");
        }
    }

    private void DisplayPlayers()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      👥 ИГРОКИ 👥                           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        if (OtherPlayers.Count == 0)
        {
            Console.WriteLine("   Нет информации об игроках.");
            Console.WriteLine("   💡 Используйте команду 'state' для получения информации.");
            return;
        }

        // Сортируем игроков: сначала текущий, затем остальные
        var sortedPlayers = OtherPlayers
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

            if (player.Id == PlayerId)
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

    public void AddToLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        GameLog.Add($"[{timestamp}] {message}");

        if (GameLog.Count > 50)
            GameLog.RemoveAt(0);

        // Пропускаем технические и информационные сообщения
        if (ShouldFilterMessage(message))
        {
            return; // Не показываем эти сообщения
        }

        // Определяем тип сообщения по иконкам/содержанию
        if (message.Contains("💥") || message.Contains("❌") || message.Contains("Ошибка"))
            Console.ForegroundColor = ConsoleColor.Red;
        else if (message.Contains("✅") || message.Contains("🎉") || message.Contains("ПОБЕДА"))
            Console.ForegroundColor = ConsoleColor.Green;
        else if (message.Contains("🎮") || message.Contains("ВАШ ХОД") || message.Contains("Сейчас ходит"))
            Console.ForegroundColor = ConsoleColor.Yellow;
        else if (message.Contains("💡") || message.Contains("Подсказка"))
            Console.ForegroundColor = ConsoleColor.Cyan;
        else if (message.Contains("⚠️") || message.Contains("Внимание") || message.Contains("таймаут"))
            Console.ForegroundColor = ConsoleColor.DarkYellow;
        else if (message.Contains("Неверный формат") || message.Contains("Не удалось"))
            Console.ForegroundColor = ConsoleColor.Red;
        else
            Console.ForegroundColor = ConsoleColor.Gray;

        Console.WriteLine($"[{timestamp}] {message}");
        Console.ResetColor();
    }

    private bool ShouldFilterMessage(string message)
    {
        // Список фраз для фильтрации
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

        // Фильтруем статистические сообщения GameStateUpdateHandler
        var gameStatePatterns = new[]
        {
        "Игроков в игре:",
        "Карт в колоде:",
        "Ходов сыграно:"
    };

        // Фильтруем сообщения об обновлении руки
        var handUpdatePatterns = new[]
        {
        "Обновлена рука. Карт:"
    };

        // Проверяем все паттерны
        foreach (var pattern in filterPatterns)
        {
            if (message.Contains(pattern))
                return true;
        }

        // Дополнительно фильтруем сообщения, которые содержат только статистику
        if (message.Contains("Игроков в игре:") ||
            message.Contains("Карт в колоде:") ||
            message.Contains("Ходов сыграно:"))
        {
            // Проверяем, не является ли это частью более важного сообщения
            if (!message.Contains("🎉") && !message.Contains("🏆") && !message.Contains("💥"))
                return true;
        }

        return false;
    }

    public async Task Stop()
    {
        Running = false;
        _cts.Cancel();

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask;
            }
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

    private async Task HandleEndTurnCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        PrintInfo("Завершаю ход...");
        await _helper.SendEndTurn(SessionId.Value, PlayerId);
    }

    private async Task HandleFavorCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 4)
        {
            Console.WriteLine("📝 Использование: favor [ID_игры] [ваш_ID] [номер_карты]");
            Console.WriteLine($"💡 Пример: favor {SessionId.Value} {PlayerId} 0");
            return;
        }

        if (!Guid.TryParse(parts[1], out var gameId) || gameId != SessionId.Value)
        {
            PrintError("Неверный ID игры");
            return;
        }

        if (!Guid.TryParse(parts[2], out var playerId) || playerId != PlayerId)
        {
            PrintError("Неверный ваш ID");
            return;
        }

        if (!int.TryParse(parts[3], out var cardIndex))
        {
            PrintError("Неверный номер карты");
            DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= Hand.Count)
        {
            PrintError($"Неверный номер карты! У вас {Hand.Count} карт (0-{Hand.Count - 1})");
            DisplayHand();
            return;
        }

        var card = Hand[cardIndex];
        PrintInfo($"📤 Отдаю карту #{cardIndex}: {card.Name}");
        await _helper.SendFavorResponse(gameId, playerId, cardIndex);
    }

    private async Task HandleGiveCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("📝 Использование: give [номер_карты]");
            DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= Hand.Count)
        {
            PrintError($"Неверный номер карты! У вас {Hand.Count} карт (0-{Hand.Count - 1})");
            DisplayHand();
            return;
        }

        var card = Hand[cardIndex];
        PrintInfo($"📤 Отдаю карту #{cardIndex}: {card.Name}");
        await _helper.SendFavorResponse(SessionId.Value, PlayerId, cardIndex);

        // Ждем немного для получения ответа от сервера
        await Task.Delay(300);

        // ПОКАЗЫВАЕМ РУКУ ПОСЛЕ ОТДАЧИ КАРТЫ
        DisplayHand();
    }

    private async Task HandleStealCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2)
        {
            Console.WriteLine("📝 Использование: steal [номер_карты]");
            Console.WriteLine("💡 Пример: steal 2");
            return;
        }

        if (!int.TryParse(parts[1], out var cardIndex))
        {
            PrintError("Неверный номер карты! Введите число.");
            return;
        }

        PrintInfo($"🎭 Краду карту #{cardIndex}...");
        await _helper.SendStealCard(SessionId.Value, PlayerId, cardIndex);
    }

    private async Task HandleTakeDiscardCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2)
        {
            Console.WriteLine("📝 Использование: takediscard [номер_карты]");
            Console.WriteLine("💡 Пример: takediscard 1");
            return;
        }

        if (!int.TryParse(parts[1], out var cardIndex))
        {
            PrintError("Неверный номер карты! Введите число.");
            return;
        }

        PrintInfo($"🎨 Беру карту #{cardIndex} из сброса...");
        await _helper.SendTakeFromDiscard(SessionId.Value, PlayerId, cardIndex);
    }

    private string? ReadLineSafe()
    {
        try
        {
            Console.CursorVisible = true;
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

    private async Task DisplayOtherPlayers()
    {
        if (OtherPlayers.Count == 0)
        {
            Console.WriteLine("⚠️  Информация о других игроках не загружена.");
            Console.WriteLine("   💡 Используйте команду 'players' для обновления списка.");
            return;
        }

        var alivePlayers = OtherPlayers
            .Where(p => p.IsAlive && p.Id != PlayerId)
            .OrderBy(p => p.Name)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            Console.WriteLine("❌ Нет других живых игроков!");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🎯 ДОСТУПНЫЕ ЦЕЛИ 🎯                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine("   Доступные цели (автоматически выбирается первая):");

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            var player = alivePlayers[i];
            if (i == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"   [⭐] ");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"   [{i}] ");
            }
            Console.ResetColor();
            Console.Write($"{player.Name,-15}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ID: {player.Id}");
            Console.ResetColor();
            Console.WriteLine($"      Карт у игрока: {player.CardCount}");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("   💡 Для выбора другой цели укажите ID в команде:");
        Console.WriteLine($"   Пример: play [номер_карты] {alivePlayers[0].Id}");
        Console.WriteLine($"   Пример: combo 2 [номера_карт] {alivePlayers[0].Id}");
        Console.ResetColor();
    }

    private bool ValidateComboCards(int comboType, List<Card> cards)
    {
        if (cards.Count != comboType) return false;

        switch (comboType)
        {
            case 2:
                return cards[0].Type == cards[1].Type ||
                       cards[0].IconId == cards[1].IconId;
            case 3:
                return (cards[0].Type == cards[1].Type && cards[1].Type == cards[2].Type) ||
                       (cards[0].IconId == cards[1].IconId && cards[1].IconId == cards[2].IconId);
            case 5:
                return cards.Select(c => c.IconId).Distinct().Count() == 5;
            default:
                return false;
        }
    }

    private void DisplayComboRules(int comboType)
    {
        Console.WriteLine("\n📚 Правила комбо:");
        switch (comboType)
        {
            case 2:
                Console.WriteLine("• 2 одинаковые карты ИЛИ");
                Console.WriteLine("• 2 карты с одинаковой иконкой");
                break;
            case 3:
                Console.WriteLine("• 3 одинаковые карты ИЛИ");
                Console.WriteLine("• 3 карты с одинаковой иконкой");
                break;
            case 5:
                Console.WriteLine("• 5 карт с РАЗНЫМИ иконками");
                break;
        }
    }

    private void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"💡 {message}");
        Console.ResetColor();
    }

    private void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ {message}");
        Console.ResetColor();
    }
}