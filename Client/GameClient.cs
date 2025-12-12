using Server.Game.Enums;
using Server.Game.Models;
using Server.Networking.Commands;
using Server.Networking.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Client;

public class GameClient
{
    private readonly Socket _socket;
    private readonly KittensClientHelper _helper;
    private readonly ClientCommandHandlerFactory _handlerFactory;

    public Guid? SessionId { get; set; }
    public Guid PlayerId { get; set; }
    public List<Card> Hand { get; } = new();
    public GameState CurrentGameState { get; set; }
    public bool Running { get; set; } = true;
    public string PlayerName { get; set; } = "Игрок";
    public List<string> GameLog { get; } = new();
    public List<PlayerInfo> OtherPlayers { get; } = new();
    private readonly List<byte> _receiveBuffer = new();


    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;

    public GameClient(string host, int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(host, port);
        _helper = new KittensClientHelper(_socket);
        _handlerFactory = new ClientCommandHandlerFactory();

        Console.WriteLine($"Подключено к серверу {host}:{port}");
    }

    public async Task Start()
    {
        // Запрашиваем имя игрока
        Console.Write("Введите ваше имя: ");
        PlayerName = Console.ReadLine()?.Trim() ?? "Игрок";

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
                    Console.WriteLine("Соединение с сервером потеряно.");
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
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        await Stop();
    }

    private async Task HandleUserInput()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n> ");
        Console.ResetColor();

        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLower();

        Console.WriteLine($"Ввод: '{input}'");
        Console.WriteLine($"Разделено на {parts.Length} частей:");
        for (int i = 0; i < parts.Length; i++)
        {
            Console.WriteLine($"  parts[{i}] = '{parts[i]}'");
        }

        try
        {
            switch (command)
            {
                case "create":
                    await HandleCreateCommand(parts);
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

                case "give": // Альтернативная команда для favor
                    await HandleGiveCommand(parts);
                    break;

                case "choose": // Альтернативное название для give
                    await HandleGiveCommand(parts); // или HandleChooseCommand если он существует
                    break;

                case "exit":
                case "quit":
                    Running = false;
                    break;

                case "end":
                    await HandleEndTurnCommand(parts);
                    break;

                default:
                    Console.WriteLine($"Неизвестная команда: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка выполнения команды: {ex.Message}");
        }
    }

    private async Task HandleCreateCommand(string[] parts)
    {
        var name = PlayerName;
        if (string.IsNullOrEmpty(name))
        {
            Console.WriteLine("Имя игрока не может быть пустым!");
            return;
        }
        await _helper.SendCreateGame(name);
    }

    private async Task HandleJoinCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Использование: join [ID_игры] [имя]");
            return;
        }

        if (!Guid.TryParse(parts[1], out var gameId))
        {
            Console.WriteLine("Неверный формат ID игры");
            return;
        }

        var name = parts.Length > 2 ? parts[2] : PlayerName;
        await _helper.SendJoinGame(gameId, name);
        Console.WriteLine($"Присоединение к игре {gameId} как {name}...");
    }

    private async Task HandleStartCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре. Сначала создайте или присоединитесь к игре.");
            return;
        }

        await _helper.SendStartGame(SessionId.Value);
        Console.WriteLine("Запуск игры...");
    }

    private async Task HandlePlayCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("Использование: play [номер_карты] [ID_целевого_игрока]");
            Console.WriteLine("Пример: play 3 550e8400-e29b-41d4-a716-446655440000");
            DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= Hand.Count)
        {
            Console.WriteLine($"Неверный номер карты. Доступны номера 0-{Hand.Count - 1}");
            return;
        }

        var card = Hand[cardIndex];
        string? targetPlayerId = parts.Length > 2 ? parts[2] : null;

        // Проверим, что targetPlayerId - это Guid
        if (targetPlayerId != null && !Guid.TryParse(targetPlayerId, out _))
        {
            Console.WriteLine("ID целевого игрока должен быть в формате GUID!");
            Console.WriteLine("Пример: 550e8400-e29b-41d4-a716-446655440000");
            return;
        }

        await _helper.SendPlayCard(SessionId.Value, PlayerId, cardIndex, targetPlayerId);
        Console.WriteLine($"Играем карту: {card.Name}");
    }

    private async Task HandleDrawCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        await _helper.SendDrawCard(SessionId.Value, PlayerId);
        Console.WriteLine("Берем карту из колоды...");
    }

    private async Task HandleComboCommand(string[] parts)
    {
        if (!SessionId.HasValue || parts.Length < 3)
        {
            Console.WriteLine("Использование: combo [тип] [номера_карт через запятую] [целевой_игрок]");
            Console.WriteLine("Типы: 2 (две одинаковые), 3 (три одинаковые), 5 (пять разных)");
            return;
        }

        if (!int.TryParse(parts[1], out var comboType) || (comboType != 2 && comboType != 3 && comboType != 5))
        {
            Console.WriteLine("Неверный тип комбо. Допустимо: 2, 3, 5");
            return;
        }

        var cardIndices = parts[2].Split(',')
            .Select(s => int.TryParse(s.Trim(), out var i) ? i : -1)
            .Where(i => i >= 0 && i < Hand.Count)
            .ToList();

        if (cardIndices.Count != comboType)
        {
            Console.WriteLine($"Для комбо типа {comboType} нужно {comboType} карт");
            return;
        }

        var cardNames = cardIndices.Select(i => Hand[i].Name);
        Console.WriteLine($"Играем комбо {comboType} с картами: {string.Join(", ", cardNames)}");

        string? targetPlayerId = parts.Length > 3 ? parts[3] : null;
        await _helper.SendUseCombo(SessionId.Value, PlayerId, comboType, cardIndices, targetPlayerId);
    }

    private async Task HandleNopeCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        await _helper.SendPlayNope(SessionId.Value, PlayerId);
        Console.WriteLine("Играем карту НЕТ!");
    }

    private async Task HandleDefuseCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        var position = parts.Length > 1 && int.TryParse(parts[1], out var pos) ? pos : 0;
        await _helper.SendPlayDefuse(SessionId.Value, PlayerId, position);
        Console.WriteLine($"Играем карту Обезвредить, позиция в колоде: {position}");
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

                // Копируем данные в новый массив
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
            Console.WriteLine("\nСоединение с сервером разорвано.");
            Running = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nОшибка приема данных: {ex.Message}");
            Running = false;
        }
    }

    // File: Client/GameClient.cs
    private async Task ProcessServerMessage(byte[] data)
    {
        Console.WriteLine($"Получено байт: {data.Length}, буфер: {_receiveBuffer.Count}");

        _receiveBuffer.AddRange(data);

        // Теперь минимальная длина пакета: START (1) + CMD (1) + LEN (2) + END (1) = 5 байт
        while (_receiveBuffer.Count >= 5)
        {
            // Ищем стартовый байт 0x02
            int startIndex = -1;
            for (int i = 0; i <= _receiveBuffer.Count - 5; i++) // -5 для минимального пакета
            {
                if (_receiveBuffer[i] == 0x02)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == -1)
            {
                Console.WriteLine("Стартовый байт не найден");
                _receiveBuffer.Clear();
                break;
            }

            if (startIndex > 0)
            {
                Console.WriteLine($"Пропускаем {startIndex} байт до стартового байта");
                _receiveBuffer.RemoveRange(0, startIndex);
                continue;
            }

            // Теперь стартовый байт точно на позиции 0
            var command = _receiveBuffer[1];

            // Читаем длину как ushort (2 байта, Little Endian)
            ushort payloadLength = (ushort)(_receiveBuffer[2] | (_receiveBuffer[3] << 8));

            // Вычисляем ожидаемую общую длину: START + CMD + LEN_SIZE + PAYLOAD + END
            var expectedTotalLength = 1 + 1 + KittensPackageMeta.LengthSize + payloadLength + 1;

            Console.WriteLine($"Пакет: команда 0x{command:X2}, длина (ushort) {payloadLength}, ожидаем {expectedTotalLength} байт, в буфере {_receiveBuffer.Count}");

            if (_receiveBuffer.Count >= expectedTotalLength)
            {
                // Проверяем конечный байт на правильной позиции
                var endIndex = expectedTotalLength - 1; // Последний байт пакета
                if (endIndex >= _receiveBuffer.Count)
                {
                    Console.WriteLine("Недостаточно данных для проверки конечного байта");
                    break; // или continue, если буфер неожиданно уменьшился
                }

                if (_receiveBuffer[endIndex] != 0x03)
                {
                    Console.WriteLine($"Неверный конечный байт: {_receiveBuffer[endIndex]:X2} на позиции {endIndex}, ожидаем 03 на позиции {expectedTotalLength - 1}");
                    // Возможно, пакет повреждён. Можно попробовать сдвинуться на 1 и искать дальше.
                    _receiveBuffer.RemoveAt(0);
                    continue;
                }

                // Извлекаем полный пакет
                var packet = _receiveBuffer.Take(expectedTotalLength).ToArray();
                _receiveBuffer.RemoveRange(0, expectedTotalLength);

                Console.WriteLine($"Обрабатываем пакет длиной {packet.Length} байт");

                var parsed = KittensPackageParser.TryParse(packet, out var error);
                if (parsed != null)
                {
                    var (cmd, payload) = parsed.Value;
                    Console.WriteLine($"Пакет успешно разобран: команда {cmd}");

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
                else
                {
                    Console.WriteLine($"Ошибка парсинга: {error}");
                }
            }
            else
            {
                Console.WriteLine($"Недостаточно данных: нужно {expectedTotalLength}, есть {_receiveBuffer.Count}");
                break; // Ждем больше данных
            }
        }
    }

    private async Task HandleCommandFallback(Command command, byte[] payload)
    {
        switch (command)
        {
            case Command.Message:
                var message = Encoding.UTF8.GetString(payload);
                AddToLog($"Сообщение: {message}");
                break;

            case Command.Error:
                if (payload.Length > 0)
                {
                    var error = (CommandResponse)payload[0];
                    AddToLog($"Ошибка: {error}");
                }
                break;

            default:
                AddToLog($"Необработанная команда: {command}");
                break;
        }

        await Task.CompletedTask;
    }

    private void DisplayHelp()
    {
        Console.Clear();
        Console.WriteLine("=== ВЗРЫВНЫЕ КОТЯТА ===");
        Console.WriteLine();
        Console.WriteLine("Основные команды:");
        Console.WriteLine("  create [имя]          - Создать новую игру");
        Console.WriteLine("  join [ID] [имя]       - Присоединиться к игре");
        Console.WriteLine("  start                 - Начать игру (если создатель)");
        Console.WriteLine("  play [номер] [цель]   - Сыграть карту");
        Console.WriteLine("  draw                  - Взять карту из колоды");
        Console.WriteLine("  combo 2 [1,2] [цель]  - Сыграть комбо (2 одинаковые или с одинаковой иконкой)");
        Console.WriteLine("  combo 3 [1,2,3] [цель] - Сыграть комбо (3 одинаковые или с одинаковой иконкой)");
        Console.WriteLine("  combo 5 [1,2,3,4,5]   - Сыграть комбо (5 разных с разными иконками)");
        Console.WriteLine("  nope                  - Сыграть карту НЕТ");
        Console.WriteLine("  defuse [позиция]      - Обезвредить котенка");
        Console.WriteLine("  give [номер]          - Отдать карту при запросе 'Одолжения'");
        Console.WriteLine("  hand                  - Показать карты на руке");
        Console.WriteLine("  state                 - Показать состояние игры");
        Console.WriteLine("  players               - Показать игроков");
        Console.WriteLine("  help                  - Показать эту справку");
        Console.WriteLine("  exit                  - Выйти из игры");
        Console.WriteLine();
        Console.WriteLine("ПРАВИЛА ХОДА:");
        Console.WriteLine("  • Можно играть любое количество карт за ход");
        Console.WriteLine("  • В конце хода ОБЯЗАТЕЛЬНО взять карту из колоды (draw)");
        Console.WriteLine("  • Исключения:");
        Console.WriteLine("      - Карта 'Пропустить' завершает ход БЕЗ взятия карты");
        Console.WriteLine("      - Карта 'Атаковать' завершает ход БЕЗ взятия карты");
        Console.WriteLine("      - Следующий игрок после 'Атаковать' ходит ДВАЖДЫ");
        Console.WriteLine("  • После взятия карты (draw) ход автоматически переходит следующему");
        Console.WriteLine();
        Console.WriteLine("КАРТЫ:");
        Console.WriteLine("  • Заглянуть в будущее - показывает 3 верхние карты колоды");
        Console.WriteLine("  • Атаковать - заканчивает ваш ход, следующий игрок ходит дважды");
        Console.WriteLine("  • Пропустить - заканчивает ход без взятия карты");
        Console.WriteLine("  • Нет - отменяет действие любой карты (кроме Взрывного Котенка и Обезвредить)");
        Console.WriteLine("  • Одолжение - берет карту у другого игрока (у него 30 сек на выбор)");
        Console.WriteLine("  • Перемешать - перемешивает колоду");
        Console.WriteLine("  • Обезвредить - спасает от Взрывного Котенка");
        Console.WriteLine("  • Карты котиков - играются только в комбо");
        Console.WriteLine();
        Console.WriteLine("КОМБО (карты котиков):");
        Console.WriteLine("  • 2 одинаковые - взять случайную карту у другого игрока");
        Console.WriteLine("  • 3 одинаковые - запросить конкретную карту у другого игрока");
        Console.WriteLine("  • 5 разных - взять любую карту из колоды сброса");
        Console.WriteLine();
        Console.WriteLine("ПРИМЕРЫ КОМАНД:");
        Console.WriteLine("  create Иван             - Создать игру");
        Console.WriteLine("  join 123abc Петр        - Присоединиться к игре");
        Console.WriteLine("  play 0                  - Сыграть первую карту");
        Console.WriteLine("  play 1 550e8400...      - Сыграть карту на игрока с ID");
        Console.WriteLine("  draw                    - Взять карту из колоды");
        Console.WriteLine("  give 2                  - Отдать третью карту при 'Одолжении'");
        Console.WriteLine("  combo 2 0,1 550e8400... - Сыграть комбо из 2 карт");
        Console.WriteLine();
    }

    public void DisplayHand()
    {
        Console.WriteLine("\n=== ВАШИ КАРТЫ ===");
        if (Hand.Count == 0)
        {
            Console.WriteLine("У вас нет карт.");
            return;
        }

        for (int i = 0; i < Hand.Count; i++)
        {
            var card = Hand[i];
            Console.ForegroundColor = GetCardColor(card.Type);
            Console.WriteLine($"{i}. {card.Name} - {card.Description}");
            Console.ResetColor();
        }
        Console.WriteLine("==================");
    }

    private void DisplayPlayers()
    {
        Console.WriteLine("\n=== ИГРОКИ ===");
        foreach (var player in OtherPlayers)
        {
            var status = player.IsAlive ? "жив" : "выбыл";
            var current = player.IsCurrentPlayer ? " ← сейчас ходит" : "";
            Console.WriteLine($"{player.Name} ({status}){current}");
            Console.WriteLine($"  ID: {player.Id}");
            Console.WriteLine($"  Карт: {player.CardCount}");
            Console.WriteLine();
        }
        Console.WriteLine("==============");
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

        // Ограничиваем размер лога
        if (GameLog.Count > 50)
            GameLog.RemoveAt(0);

        // Выводим сообщение
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[{timestamp}] {message}");
        Console.ResetColor();
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

        Console.WriteLine("Клиент остановлен.");
    }

    private async Task HandleEndTurnCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        await _helper.SendEndTurn(SessionId.Value, PlayerId);
        Console.WriteLine("Завершение хода...");
    }

    private async Task HandleChooseCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("Использование: choose [номер_карты]");
            Console.WriteLine("Или: give [номер_карты]");
            return;
        }

        // Отправляем выбор карты серверу
        await _helper.SendChooseCard(SessionId.Value, PlayerId, cardIndex);
        Console.WriteLine($"Отдаем карту #{cardIndex}");
    }

    private async Task HandleFavorCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        if (parts.Length < 4)
        {
            Console.WriteLine("❌ Использование: favor [ID_игры] [ваш_ID] [номер_карты]");
            Console.WriteLine($"📋 Пример: favor {SessionId.Value} {PlayerId} 0");
            return;
        }

        if (!Guid.TryParse(parts[1], out var gameId) || gameId != SessionId.Value)
        {
            Console.WriteLine("❌ Неверный ID игры");
            return;
        }

        if (!Guid.TryParse(parts[2], out var playerId) || playerId != PlayerId)
        {
            Console.WriteLine("❌ Неверный ваш ID");
            return;
        }

        if (!int.TryParse(parts[3], out var cardIndex))
        {
            Console.WriteLine("❌ Неверный номер карты");
            DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= Hand.Count)
        {
            Console.WriteLine($"❌ Неверный номер карты! У вас {Hand.Count} карт (0-{Hand.Count - 1})");
            DisplayHand();
            return;
        }

        var card = Hand[cardIndex];
        Console.WriteLine($"📤 Отдаю карту #{cardIndex}: {card.Name}");

        await _helper.SendFavorResponse(gameId, playerId, cardIndex);
    }

    private async Task HandleGiveCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("❌ Использование: give [номер_карты]");
            Console.WriteLine($"💡 Или используйте: favor {SessionId.Value} {PlayerId} [номер_карты]");
            DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= Hand.Count)
        {
            Console.WriteLine($"❌ Неверный номер карты! У вас {Hand.Count} карт (0-{Hand.Count - 1})");
            DisplayHand();
            return;
        }

        var card = Hand[cardIndex];
        Console.WriteLine($"📤 Отдаю карту #{cardIndex}: {card.Name}");

        // Используем сокращенную команду (требует SessionId и PlayerId)
        await _helper.SendFavorResponse(SessionId.Value, PlayerId, cardIndex);
    }
}
