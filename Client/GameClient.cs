using Client.ClientHandlers;
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

    public Guid? _lastActiveActionId = null;


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

        // Используйте ReadLineSafe вместо Console.ReadLine()
        var input = ReadLineSafe();
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

                case "steal": // ← ДОБАВЬТЕ ЭТУ СТРОЧКУ!
                    await HandleStealCommand(parts);
                    break;

                case "takediscard":
                    await HandleTakeDiscardCommand(parts);
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
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        if (parts.Length < 3)
        {
            Console.WriteLine("❌ Использование: combo [тип] [номера_карт через запятую]");
            Console.WriteLine("💡 Примеры:");
            Console.WriteLine("  combo 2 0,1");
            Console.WriteLine("  combo 3 0,1,2");
            Console.WriteLine("  combo 5 0,1,2,3,4");
            return;
        }

        if (!int.TryParse(parts[1], out var comboType) || (comboType != 2 && comboType != 3 && comboType != 5))
        {
            Console.WriteLine("❌ Неверный тип комбо. Допустимо: 2, 3, 5");
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
            Console.WriteLine($"❌ Для комбо типа {comboType} нужно {comboType} разных карт");
            Console.WriteLine($"   Указано: {cardIndices.Count} карт");
            return;
        }

        // Проверяем, что карты подходят для комбо
        var comboCards = cardIndices.Select(i => Hand[i]).ToList();
        if (!ValidateComboCards(comboType, comboCards))
        {
            Console.WriteLine($"❌ Выбранные карты не подходят для комбо {comboType}");
            DisplayComboRules(comboType);
            return;
        }

        var cardNames = comboCards.Select(c => c.Name);
        Console.WriteLine($"🎭 Играем комбо {comboType} с картами: {string.Join(", ", cardNames)}");

        string? targetData = null;

        switch (comboType)
        {
            case 2:
                // Для комбо 2 нужен только ID цели
                if (parts.Length > 3)
                {
                    targetData = parts[3];
                }
                else
                {
                    // Запрашиваем ID цели
                    await DisplayOtherPlayers();
                    Console.Write("\nВведите ID целевого игрока: ");
                    var targetId = ReadLineSafe();

                    if (string.IsNullOrEmpty(targetId) || !Guid.TryParse(targetId, out _))
                    {
                        Console.WriteLine("❌ Неверный ID игрока!");
                        return;
                    }
                    targetData = targetId;
                }
                Console.WriteLine($"✅ Цель для комбо 2: {targetData}");
                break;

            case 3:
                // Для комбо 3 нужен ID цели и название карты
                if (parts.Length > 3)
                {
                    targetData = parts[3];
                    if (parts.Length > 4)
                    {
                        targetData += $"|{parts[4]}";
                    }
                }
                else
                {
                    Console.WriteLine("❌ Для комбо 3 укажите ID цели и название карты");
                    Console.WriteLine("💡 Пример: combo 3 0,1,2 [ID_цели] [название_карты]");
                    return;
                }
                break;

            case 5:
                // Для комбо 5 нет целевых данных
                break;
        }

        try
        {
            Console.WriteLine($"📤 Отправка комбо на сервер...");

            // Отправляем индексы карт
            var indicesStr = string.Join(",", cardIndices);
            Console.WriteLine($"DEBUG: Отправляемые индексы карт: {indicesStr}");

            // Используем существующий метод SendUseCombo
            await _helper.SendUseCombo(SessionId.Value, PlayerId, comboType, cardIndices, targetData);

            Console.WriteLine($"✅ Команда комбо отправлена!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка отправки комбо: {ex.Message}");
        }
    }


    // Новый метод: Получение цели для комбо 2
    private async Task<string?> GetTargetForCombo2()
    {
        Console.WriteLine("\n══════════════════════════════════════════");
        Console.WriteLine("🎭 КОМБО 2: СЛЕПОЙ КАРМАННИК");
        Console.WriteLine("══════════════════════════════════════════");

        await DisplayOtherPlayers();

        // Если нет других игроков, нельзя играть комбо 2
        var alivePlayers = OtherPlayers
            .Where(p => p.IsAlive && p.Id != PlayerId)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            Console.WriteLine("❌ Нет других живых игроков для использования комбо!");
            return null;
        }

        Console.Write("\nВведите ID целевого игрока: ");
        var targetId = ReadLineSafe();

        if (string.IsNullOrEmpty(targetId))
        {
            Console.WriteLine("❌ ID игрока не может быть пустым");
            return null;
        }

        if (!Guid.TryParse(targetId, out var parsedGuid))
        {
            Console.WriteLine("❌ ID должен быть в формате GUID");
            Console.WriteLine("💡 Пример: 550e8400-e29b-41d4-a716-446655440000");
            return null;
        }

        // Проверяем, что ID принадлежит живому игроку (кроме себя)
        var targetPlayer = OtherPlayers.FirstOrDefault(p =>
            p.Id == parsedGuid && p.IsAlive && p.Id != PlayerId);

        if (targetPlayer == null)
        {
            Console.WriteLine("❌ Указанный игрок не найден, не жив или это вы сами!");
            return null;
        }

        Console.WriteLine($"✅ Цель найдена: {targetPlayer.Name}");
        Console.WriteLine($"🎯 Крадем СЛУЧАЙНУЮ карту у {targetPlayer.Name} ({targetId})");

        return targetId;
    }

    // Новый метод: Получение цели для комбо 3
    private async Task<string?> GetTargetForCombo3()
    {
        Console.WriteLine("\n══════════════════════════════════════════");
        Console.WriteLine("🎣 КОМБО 3: ВРЕМЯ РЫБАЧИТЬ");
        Console.WriteLine("══════════════════════════════════════════");

        // Вызываем метод, который мы только что создали
        await DisplayOtherPlayers();

        // Если нет других игроков, нельзя играть комбо 3
        var alivePlayers = OtherPlayers
            .Where(p => p.IsAlive && p.Id != PlayerId)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            Console.WriteLine("❌ Нет других живых игроков для использования комбо!");
            return null;
        }

        Console.Write("\nВведите ID целевого игрока: ");
        var targetId = ReadLineSafe();

        if (string.IsNullOrEmpty(targetId))
        {
            Console.WriteLine("❌ ID игрока не может быть пустым");
            return null;
        }

        if (!Guid.TryParse(targetId, out var parsedGuid))
        {
            Console.WriteLine("❌ ID должен быть в формате GUID");
            Console.WriteLine("💡 Пример: 550e8400-e29b-41d4-a716-446655440000");
            return null;
        }

        // Проверяем, что ID принадлежит живому игроку (кроме себя)
        var targetPlayer = OtherPlayers.FirstOrDefault(p =>
            p.Id == parsedGuid && p.IsAlive && p.Id != PlayerId);

        if (targetPlayer == null)
        {
            Console.WriteLine("❌ Указанный игрок не найден, не жив или это вы сами!");
            return null;
        }

        Console.WriteLine($"✅ Цель найдена: {targetPlayer.Name}");

        Console.WriteLine("\n══════════════════════════════════════════");
        Console.WriteLine("📋 ВЫБЕРИТЕ КАРТУ ПО НОМЕРУ:");
        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine(" 1. Взрывной Котенок");
        Console.WriteLine(" 2. Обезвредить");
        Console.WriteLine(" 3. Нет");
        Console.WriteLine(" 4. Атаковать");
        Console.WriteLine(" 5. Пропустить");
        Console.WriteLine(" 6. Одолжение");
        Console.WriteLine(" 7. Перемешать");
        Console.WriteLine(" 8. Заглянуть в будущее");
        Console.WriteLine(" 9. Радужный Кот");
        Console.WriteLine("10. Котобородач");
        Console.WriteLine("11. Кошка-Картошка");
        Console.WriteLine("12. Арбузный Котэ");
        Console.WriteLine("13. Такокот");
        Console.WriteLine("══════════════════════════════════════════");

        Console.Write("\nВведите номер карты (1-13): ");
        var cardNumberInput = ReadLineSafe();

        if (!int.TryParse(cardNumberInput, out var cardNumber) || cardNumber < 1 || cardNumber > 13)
        {
            Console.WriteLine("❌ Неверный номер карты. Введите число от 1 до 13");
            return null;
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
            _ => "Обезвредить" // fallback
        };

        Console.WriteLine($"🎯 Запрашиваем карту '{cardName}' у игрока {targetPlayer.Name} ({targetId})");
        Console.WriteLine($"DEBUG: Формируемая строка: '{targetId}|{cardName}'");

        return $"{targetId}|{cardName}";
    }

    // Новый метод: Проверка карт на соответствие комбо
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
        Console.WriteLine("\n📚 ПРАВИЛА КОМБО:");
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



    private async Task HandleNopeCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        Guid actionId;

        // Если команда просто "nope" без параметров
        if (parts.Length == 1)
        {
            if (_lastActiveActionId.HasValue)
            {
                Console.WriteLine($"💡 Использую последнее действие: {_lastActiveActionId}");
                actionId = _lastActiveActionId.Value;
            }
            else
            {
                Console.WriteLine("❌ Необходимо указать ID действия!");
                Console.WriteLine("💡 ID действия можно увидеть в сообщении об атаке/комбо");
                Console.WriteLine("📋 Формат: nope [ID_действия]");
                return;
            }
        }
        else if (!Guid.TryParse(parts[1], out actionId))
        {
            Console.WriteLine("❌ Неверный формат ID действия!");
            return;
        }

        Console.WriteLine($"🚫 Играю карту НЕТ на действие {actionId}");
        await _helper.SendPlayNope(SessionId.Value, PlayerId, actionId);
    }

    private async Task HandleDefuseCommand(string[] parts)
    {
        if (!SessionId.HasValue || PlayerId == Guid.Empty)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        // Всегда используем текущие ID сессии и игрока
        int position = 0;

        if (parts.Length < 2)
        {
            Console.WriteLine("❌ Использование: defuse [позиция]");
            Console.WriteLine($"💡 Пример: defuse 0 (положить наверх)");
            Console.WriteLine($"💡 Пример: defuse 4 (положить на 5-ю позицию)");
            return;
        }

        if (!int.TryParse(parts[1], out position))
        {
            Console.WriteLine("❌ Неверная позиция! Используйте число 0-5");
            return;
        }

        // Ограничиваем позицию
        position = Math.Min(position, 5);

        Console.WriteLine($"💣 Отправка команды...");
        Console.WriteLine($"   Game ID: {SessionId}");
        Console.WriteLine($"   Player ID: {PlayerId}");
        Console.WriteLine($"   Position: {position}");

        await _helper.SendPlayDefuse(SessionId.Value, PlayerId, position);
        Console.WriteLine($"✅ Команда отправлена!");
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

    // Метод для получения списка игроков (можно вызвать из GameStateUpdateHandler)
    public void UpdatePlayersList(List<PlayerInfo> players)
    {
        OtherPlayers.Clear();
        OtherPlayers.AddRange(players);

        // Также добавляем себя в список для полноты
        OtherPlayers.Add(new PlayerInfo
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
        Console.WriteLine("  combo 3 [1,2,3] [цель] [карта] - Сыграть комбо (3 одинаковые или с одинаковой иконкой)");
        Console.WriteLine("  combo 5 [1,2,3,4,5]   - Сыграть комбо (5 разных с разными иконками)");
        Console.WriteLine("  nope                  - Сыграть карту НЕТ");
        Console.WriteLine("  defuse [позиция]      - Обезвредить котенка");
        Console.WriteLine("  give [номер]          - Отдать карту при запросе 'Одолжения'");
        Console.WriteLine("  steal [номер]         - Выбрать карту при 'Слепом карманнике'");
        Console.WriteLine("  takediscard [номер]   - Выбрать карту из сброса при комбо 5");
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
        Console.WriteLine("  • 2 одинаковые (СЛЕПОЙ КАРМАННИК) - украсть карту ВСЛЕПУЮ у другого игрока");
        Console.WriteLine("      • Целевой игрок показывает карты рубашкой вверх");
        Console.WriteLine("      • Вы выбираете карту наугад по номеру");
        Console.WriteLine("      ФОРМАТ:");
        Console.WriteLine("        1. combo 2 [номера_ваших_карт] [ID_целевого_игрока] - Начать кражу");
        Console.WriteLine("        2. Вам покажут скрытые карты цели");
        Console.WriteLine("        3. steal [номер_карты] - Выбрать карту по номеру");
        Console.WriteLine("      ПРИМЕР:");
        Console.WriteLine("        combo 2 0,1 550e8400-e29b-41d4-a716-446655440000");
        Console.WriteLine("        (после показа карт) steal 2");
        Console.WriteLine();
        Console.WriteLine("  • 3 одинаковые (ВРЕМЯ РЫБАЧИТЬ) - запросить КОНКРЕТНУЮ карту у другого игрока");
        Console.WriteLine("      • Назовите карту, которую хотите получить");
        Console.WriteLine("      • Если у цели есть эта карта - вы её забираете");
        Console.WriteLine("      • Если нет - ничего не получаете");
        Console.WriteLine("      ФОРМАТ: combo 3 [номера_карт] [ID_целевого_игрока] [название_карты]");
        Console.WriteLine("      ПРИМЕРЫ:");
        Console.WriteLine("        combo 3 0,1,2 550e8400... Такокот");
        Console.WriteLine("        combo 3 0,1,2 550e8400... Атаковать");
        Console.WriteLine("        combo 3 0,1,2 550e8400... Обезвредить");
        Console.WriteLine("        combo 3 0,1,2 550e8400... Заглянуть в будущее");
        Console.WriteLine("        combo 3 0,1,2 550e8400... 'Заглянуть в будущее' (в кавычках для названий с пробелами)");
        Console.WriteLine();
        Console.WriteLine("  • 5 разных (ВОРУЙ ИЗ КОЛОДЫ СБРОСА) - взять карту из колоды сброса");
        Console.WriteLine("      • Показываются все карты в сбросе");
        Console.WriteLine("      • Вы выбираете любую карту");
        Console.WriteLine("      ФОРМАТ: combo 5 [номера_карт]");
        Console.WriteLine("      ПРИМЕР: combo 5 0,1,2,3,4");
        Console.WriteLine();
        Console.WriteLine("ДОСТУПНЫЕ НАЗВАНИЯ КАРТ:");
        Console.WriteLine("  • Взрывной Котенок    • Пропустить          • Радужный Кот");
        Console.WriteLine("  • Обезвредить         • Одолжение          • Котобородач");
        Console.WriteLine("  • Нет                 • Перемешать         • Кошка-Картошка");
        Console.WriteLine("  • Атаковать           • Заглянуть в будущее • Арбузный Котэ");
        Console.WriteLine("                                              • Такокот");
        Console.WriteLine();
        Console.WriteLine("ПРИМЕРЫ КОМАНД:");
        Console.WriteLine("  create Иван             - Создать игру");
        Console.WriteLine("  join 123abc Петр        - Присоединиться к игре");
        Console.WriteLine("  play 0                  - Сыграть первую карту");
        Console.WriteLine("  play 1 550e8400...      - Сыграть карту на игрока с ID");
        Console.WriteLine("  draw                    - Взять карту из колоды");
        Console.WriteLine("  give 2                  - Отдать третью карту при 'Одолжении'");
        Console.WriteLine("  steal 2                 - Выбрать карту #2 при 'Слепом карманнике'");
        Console.WriteLine("  takediscard 1           - Выбрать карту #1 из сброса");
        Console.WriteLine();
        Console.WriteLine("ПОЛНЫЙ ПРИМЕР КОМБО 2:");
        Console.WriteLine("  1. hand                 - Смотрите свои карты");
        Console.WriteLine("  2. combo 2 0,1 abcdef... - Играете две одинаковые карты на игрока");
        Console.WriteLine("  3. Вам показывают: ❓❓❓❓ (4 скрытые карты)");
        Console.WriteLine("  4. steal 2              - Выбираете карту #2 наугад");
        Console.WriteLine("  5. Вы получаете случайную карту, цель теряет её");
        Console.WriteLine();
        Console.WriteLine("ПОЛНЫЙ ПРИМЕР КОМБО 3:");
        Console.WriteLine("  1. players              - Смотрите ID других игроков");
        Console.WriteLine("  2. hand                 - Смотрите свои карты");
        Console.WriteLine("  3. combo 3 0,1,2 abcdef... Такокот - Запрашиваете карту Такокот");
        Console.WriteLine("  4. Сервер проверяет, есть ли у цели карта 'Такокот'");
        Console.WriteLine("  5. Если есть - вы забираете её, если нет - ничего не получаете");
        Console.WriteLine();
        Console.WriteLine("ПОЛНЫЙ ПРИМЕР КОМБО 5:");
        Console.WriteLine("  1. combo 5 0,1,2,3,4    - Играете 5 разных карт");
        Console.WriteLine("  2. Вам показывают карты в сбросе с номерами");
        Console.WriteLine("  3. takediscard 2        - Выбираете карту #2 из сброса");
        Console.WriteLine("  4. Вы получаете выбранную карту");
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

    private async Task HandleStealCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        // УПРОЩАЕМ: команда должна быть просто "steal [номер_карты]"
        // Все остальные данные (ID игры, ID игрока, ID цели) уже известны из контекста комбо 2
        if (parts.Length < 2)
        {
            Console.WriteLine("❌ Использование: steal [номер_карты]");
            Console.WriteLine($"💡 Пример: steal 2");
            Console.WriteLine($"📝 Выберите номер скрытой карты (0, 1, 2, ...)");
            return;
        }

        if (!int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("❌ Неверный номер карты! Введите число.");
            return;
        }

        Console.WriteLine($"🎭 Краду карту #{cardIndex}...");

        // Отправляем на сервер ТОЛЬКО номер карты
        // Сервер сам знает остальные данные из контекста (из PendingStealAction)
        await _helper.SendStealCard(SessionId.Value, PlayerId, cardIndex);
    }

    private async Task HandleTakeDiscardCommand(string[] parts)
    {
        if (!SessionId.HasValue)
        {
            Console.WriteLine("Вы не в игре.");
            return;
        }

        // Команда: takediscard [номер_карты]
        if (parts.Length < 2)
        {
            Console.WriteLine("❌ Использование: takediscard [номер_карты]");
            Console.WriteLine($"💡 Пример: takediscard 1");
            return;
        }

        if (!int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("❌ Неверный номер карты! Введите число.");
            return;
        }

        Console.WriteLine($"🎨 Беру карту #{cardIndex} из сброса...");

        // Отправляем на сервер ТОЛЬКО номер карты
        await _helper.SendTakeFromDiscard(SessionId.Value, PlayerId, cardIndex);
    }

    private string? ReadLineSafe()
    {
        try
        {
            var input = Console.ReadLine();

            // Отладка
            Console.WriteLine($"DEBUG ReadLineSafe: получено {input?.Length ?? 0} символов");

            if (input != null)
            {
                // Проверяем на нулевые символы
                if (input.Any(c => c == '\0'))
                {
                    Console.WriteLine($"⚠️  Обнаружены нулевые символы, заменяем...");
                    input = new string(input.Where(c => c != '\0').ToArray());
                }

                // Также проверяем другие проблемные символы
                input = input.Replace("\0", ""); // Еще раз на всякий случай
            }

            return input?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка чтения ввода: {ex.Message}");
            return null;
        }
    }

    private async Task DisplayOtherPlayers()
    {
        if (OtherPlayers.Count == 0)
        {
            Console.WriteLine("⚠️  Информация о других игроках не загружена.");
            Console.WriteLine("   Используйте команду 'players' для обновления списка.");
            return;
        }

        Console.WriteLine("👥 ДРУГИЕ ИГРОКИ:");
        Console.WriteLine("══════════════════════════════════════════");

        // Фильтруем: только живые игроки, не текущий игрок
        var alivePlayers = OtherPlayers
            .Where(p => p.IsAlive && p.Id != PlayerId)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            Console.WriteLine("❌ Нет других живых игроков!");
            return;
        }

        foreach (var player in alivePlayers)
        {
            Console.WriteLine($"• {player.Name}");
            Console.WriteLine($"  ID: {player.Id}");
            Console.WriteLine($"  Карт: {player.CardCount}");
            Console.WriteLine();
        }

        Console.WriteLine($"Всего доступных целей: {alivePlayers.Count}");
    }
}
