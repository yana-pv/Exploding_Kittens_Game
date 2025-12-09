using Common;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Client
{
    public class GameClient
    {
        private Socket? socket;
        private string playerName = "";
        private readonly byte[] buffer = new byte[8192];

        // События
        private Action<GameStartInfo>? onGameStarted;
        private Action<GameUpdate>? onGameUpdate;
        private Action<CardPlayResult>? onCardPlayed;
        private Action<string>? onPlayerEliminated;
        private Action<string>? onGameOver;
        private Action<string>? onNeedDefuse;
        private Action<string>? onNeedTarget;
        private Action<List<Card>>? onShowFuture;
        private Action<string>? onError;

        // Состояние игры
        public List<Card> Hand { get; private set; } = new();
        public bool IsConnected => socket?.Connected ?? false;
        public bool IsMyTurn { get; set; }
        public bool IsGameOver { get; private set; }
        public bool CanPlayNope { get; private set; }
        public bool MustDrawCard { get; private set; }
        public bool CanTakeFromDiscard { get; private set; }
        public int HandCount => Hand.Count;
        public bool IsGameStarted { get; set; }


        // Текущее состояние
        private GameState currentState = new();

        // --- НОВОЕ: Номер последнего полученного обновления ---
        private long _lastReceivedUpdateNumber = -1; // Начинаем с -1, чтобы первое обновление (0) всегда прошло
        private readonly object _updateNumberLock = new object(); // Защита при многопоточности
        // ---

        public class GameState
        {
            public string CurrentPlayer { get; set; } = "";
            public int DeckCount { get; set; }
            public int DiscardCount { get; set; }
            public List<string> AlivePlayers { get; set; } = new();
            public Dictionary<string, int> PlayerCardCounts { get; set; } = new();
            public string LastAction { get; set; } = "";
            public bool NeedTarget { get; set; }
            public string ActionType { get; set; } = "";
        }

        public void SetupEventHandlers(
            Action<GameStartInfo> gameStarted,
            Action<GameUpdate> gameUpdate,
            Action<CardPlayResult> cardPlayed,
            Action<string> playerEliminated,
            Action<string> gameOver,
            Action<string> needDefuse,
            Action<string> needTarget,
            Action<List<Card>> showFuture,
            Action<string> error)
        {
            onGameStarted = gameStarted;
            onGameUpdate = gameUpdate;
            onCardPlayed = cardPlayed;
            onPlayerEliminated = playerEliminated;
            onGameOver = gameOver;
            onNeedDefuse = needDefuse;
            onNeedTarget = needTarget;
            onShowFuture = showFuture;
            onError = error;
        }

        public async Task Connect(string ipAddress, string nickname)
        {
            playerName = nickname;
            Console.WriteLine($"DEBUG_CLIENT: Подключение как '{playerName}' к {ipAddress}:5001");

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Настройки сокета
                socket.NoDelay = true;

                Console.WriteLine($"DEBUG_CLIENT: Сокет создан, подключение...");

                await socket.ConnectAsync(ipAddress, 5001);
                Console.WriteLine($"DEBUG_CLIENT: Успешно подключен");

                // Отправляем имя
                var nicknameBytes = Encoding.UTF8.GetBytes(nickname);
                var packet = new Packet
                {
                    Command = ExplodingKittensProtocol.CONNECT,
                    Data = nicknameBytes
                };

                var bytes = packet.ToBytes();
                Console.WriteLine($"DEBUG_CLIENT: Отправка CONNECT пакета, размер: {bytes.Length}");

                await socket.SendAsync(bytes, SocketFlags.None);
                Console.WriteLine($"DEBUG_CLIENT: Пакет отправлен");

                // Запускаем прием сообщений
                _ = Task.Run(() => ReceiveMessages());
                Console.WriteLine($"DEBUG_CLIENT: ReceiveMessages запущен");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка подключения: {ex.Message}");
                onError?.Invoke($"Ошибка подключения: {ex.Message}");
                throw;
            }
        }

        public async Task StartGame(int playerCount)
        {
            if (!IsConnected)
            {
                onError?.Invoke("Нет подключения к серверу");
                return;
            }

            var bytes = BitConverter.GetBytes(playerCount);
            await SendPacket(ExplodingKittensProtocol.START_GAME, bytes);
        }

        public async Task PlayCard(Card card, List<Card>? combo = null, string target = "")
        {
            if (card == null)
            {
                onError?.Invoke("Карта не указана");
                return;
            }

            if (!IsMyTurn && card.Type != CardType.Nope)
            {
                onError?.Invoke("Сейчас не ваш ход");
                return;
            }

            var request = new CardPlayRequest
            {
                Card = card,
                ComboCards = combo ?? new List<Card>(),
                TargetPlayer = target
            };

            try
            {
                var json = JsonSerializer.Serialize(request);
                await SendPacket(ExplodingKittensProtocol.PLAY_CARD, Encoding.UTF8.GetBytes(json));

                // Убираем карту из руки
                if (card.Type != CardType.Nope || IsMyTurn)
                {
                    Hand.Remove(card);
                }

                // Убираем карты комбо
                if (combo != null)
                {
                    foreach (var comboCard in combo)
                    {
                        Hand.Remove(comboCard);
                    }
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Ошибка при игре карты: {ex.Message}");
            }
        }

        public async Task PlayCardByIndex(int index)
        {
            if (index < 0 || index >= Hand.Count)
            {
                onError?.Invoke($"Неверный индекс карты. Доступно: 0-{Hand.Count - 1}");
                return;
            }

            var card = Hand[index];
            await PlayCard(card);
        }

        public async Task PlayComboByIndexes(int mainIndex, List<int> comboIndexes)
        {
            if (mainIndex < 0 || mainIndex >= Hand.Count)
            {
                onError?.Invoke($"Неверный индекс основной карты");
                return;
            }

            var mainCard = Hand[mainIndex];
            var comboCards = new List<Card>();

            foreach (var index in comboIndexes)
            {
                if (index >= 0 && index < Hand.Count && index != mainIndex)
                {
                    comboCards.Add(Hand[index]);
                }
            }

            await PlayCard(mainCard, comboCards);
        }

        public async Task DrawCard(int? placement = null)
        {
            byte[] data;
            if (placement.HasValue)
            {
                data = BitConverter.GetBytes(placement.Value);
            }
            else
            {
                data = Array.Empty<byte>();
            }

            await SendPacket(ExplodingKittensProtocol.DRAW_CARD, data);
        }

        public async Task PlayNope()
        {
            // Ищем карту "Нет" в руке
            var nopeCard = Hand.FirstOrDefault(c => c.Type == CardType.Nope);
            if (nopeCard == null)
            {
                onError?.Invoke("У вас нет карты 'Нет'");
                return;
            }

            await PlayCard(nopeCard);
        }

        public async Task DefuseKitten(int placement)
        {
            var data = BitConverter.GetBytes(placement);
            await SendPacket(ExplodingKittensProtocol.DEFUSE_KITTEN, data);
        }

        public async Task SelectTarget(string targetPlayer, string requestedCard = "")
        {
            if (string.IsNullOrWhiteSpace(targetPlayer))
            {
                onError?.Invoke("Не указан целевой игрок");
                return;
            }

            var data = Encoding.UTF8.GetBytes($"{targetPlayer}|{requestedCard}");
            await SendPacket(ExplodingKittensProtocol.SELECT_TARGET, data);
        }

        public async Task TakeFromDiscard()
        {
            await SendPacket(ExplodingKittensProtocol.TAKE_FROM_DISCARD);
        }

        public void Disconnect()
        {
            try
            {
                if (socket?.Connected == true)
                {
                    SendPacket(ExplodingKittensProtocol.DISCONNECT).Wait(1000);
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
            }
            catch
            {
                // Игнорируем ошибки при отключении
            }
            finally
            {
                socket?.Dispose();
                socket = null;
            }
        }

        public void PrintHand()
        {
            Console.WriteLine("\n=== ВАША РУКА ===");
            if (!Hand.Any())
            {
                Console.WriteLine("(пусто)");
                return;
            }

            for (int i = 0; i < Hand.Count; i++)
            {
                var card = Hand[i];
                Console.WriteLine($"[{i + 1}] {GetCardDescription(card)}");
            }
        }

        public GameState GetGameState() => currentState;

        public List<string> GetAlivePlayers() => currentState.AlivePlayers;

        public bool CanPlayCardNow()
        {
            return IsMyTurn && !MustDrawCard;
        }

        private async Task ReceiveMessages()
        {
            Console.WriteLine($"DEBUG_CLIENT: Начало приема сообщений для {playerName}");

            try
            {
                while (socket != null && socket.Connected)
                {
                    Console.WriteLine($"DEBUG_CLIENT: Ожидание получения данных...");

                    try
                    {
                        var received = await socket.ReceiveAsync(buffer, SocketFlags.None);
                        Console.WriteLine($"DEBUG_CLIENT: Получено байт: {received}");

                        if (received == 0)
                        {
                            Console.WriteLine($"DEBUG_CLIENT: Соединение закрыто сервером");
                            break;
                        }

                        var packetData = new byte[received];
                        Array.Copy(buffer, 0, packetData, 0, received);

                        // ВАЖНАЯ ОТЛАДКА: выводим первые байты пакета
                        Console.WriteLine($"DEBUG_CLIENT: Первые 20 байт пакета: {BitConverter.ToString(packetData, 0, Math.Min(20, received))}");

                        try
                        {
                            var packet = Packet.FromBytes(packetData);
                            Console.WriteLine($"DEBUG_CLIENT: Успешно распакован пакет: '{packet.Command}' с {packet.Data.Length} байтами данных");

                            ProcessPacketImmediately(packet);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"DEBUG_CLIENT: Ошибка распаковки пакета: {ex.Message}");
                        }
                    }
                    catch (SocketException sockEx)
                    {
                        Console.WriteLine($"DEBUG_CLIENT: SocketException: {sockEx.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DEBUG_CLIENT: Ошибка получения: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Критическая ошибка в ReceiveMessages: {ex.Message}");
            }

            Console.WriteLine($"DEBUG_CLIENT: Завершение приема сообщений");
        }

        private void ProcessPacketImmediately(Packet packet)
        {
            Console.WriteLine($"DEBUG_CLIENT: === НАЧАЛО ОБРАБОТКИ ПАКЕТА '{packet.Command}' ===");

            try
            {
                var json = Encoding.UTF8.GetString(packet.Data);
                Console.WriteLine($"DEBUG_CLIENT: Данные пакета: {json}");

                ProcessPacket(packet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка в ProcessPacketImmediately: {ex.Message}");
            }

            Console.WriteLine($"DEBUG_CLIENT: === КОНЕЦ ОБРАБОТКИ ПАКЕТА ===");
        }

        private void ProcessPacket(Packet packet)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: === НАЧАЛО ОБРАБОТКИ ПАКЕТА '{packet.Command}' ===");

                var json = packet.Data.Length > 0 ? Encoding.UTF8.GetString(packet.Data) : "{}";

                // Обработка в зависимости от команды
                switch (packet.Command)
                {
                    case "CONNECT_RESPONSE":
                        HandleConnectResponse(json);
                        break;

                    case ExplodingKittensProtocol.GAME_STARTED:
                        HandleGameStarted(json);
                        break;

                    case ExplodingKittensProtocol.GAME_UPDATE:
                        HandleGameUpdate(json);
                        break;

                    case ExplodingKittensProtocol.CARD_PLAYED:
                        HandleCardPlayed(json);
                        break;

                    case ExplodingKittensProtocol.PLAYER_ELIMINATED:
                        HandlePlayerEliminated(json);
                        break;

                    case ExplodingKittensProtocol.GAME_OVER:
                        HandleGameOver(json);
                        break;

                    case ExplodingKittensProtocol.NEED_DEFUSE:
                        HandleNeedDefuse(json);
                        break;

                    case ExplodingKittensProtocol.REQUEST_TARGET:
                        HandleRequestTarget(json);
                        break;

                    case ExplodingKittensProtocol.REQUEST_CARD_SELECTION: // НОВОЕ
                        HandleRequestCardSelection(json);
                        break;

                    case ExplodingKittensProtocol.SHOW_FUTURE:
                        HandleShowFuture(json);
                        break;

                    case ExplodingKittensProtocol.ERROR:
                        HandleError(json);
                        break;

                    case ExplodingKittensProtocol.COMBO_RESULT:
                        HandleComboResult(json);
                        break;

                    default:
                        HandleUnknownCommand(packet.Command, json);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Критическая ошибка обработки пакета: {ex.Message}");
                Console.WriteLine($"DEBUG_CLIENT: StackTrace: {ex.StackTrace}");
            }
            finally
            {
                Console.WriteLine($"DEBUG_CLIENT: === КОНЕЦ ОБРАБОТКИ ПАКЕТА ===");
            }
        }

        #region Обработчики отдельных команд

        private void HandleConnectResponse(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: Получен CONNECT_RESPONSE");
                var response = JsonSerializer.Deserialize<dynamic>(json);
                Console.WriteLine($"DEBUG_CLIENT: Подключение подтверждено: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки CONNECT_RESPONSE: {ex.Message}");
            }
        }

        private void HandleGameStarted(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: !!! ПОЛУЧЕН GAME_STARTED !!!");

                var startInfo = JsonSerializer.Deserialize<GameStartInfo>(json);
                if (startInfo == null)
                {
                    Console.WriteLine($"DEBUG_CLIENT: Ошибка: startInfo == null");
                    return;
                }

                Console.WriteLine($"DEBUG_CLIENT: GameStartInfo десериализован");
                Console.WriteLine($"DEBUG_CLIENT: Первый игрок: {startInfo.FirstPlayer}");
                Console.WriteLine($"DEBUG_CLIENT: Количество игроков: {startInfo.PlayersCount}");

                // Обновляем руку игрока
                if (startInfo.PlayerHands.TryGetValue(playerName, out var playerHand))
                {
                    Hand = new List<Card>(playerHand);
                    Console.WriteLine($"DEBUG_CLIENT: Установлена рука с {Hand.Count} картами для {playerName}");
                }
                else
                {
                    Console.WriteLine($"DEBUG_CLIENT: Внимание: рука для {playerName} не найдена!");
                    Console.WriteLine($"DEBUG_CLIENT: Доступные игроки: {string.Join(", ", startInfo.PlayerHands.Keys)}");
                }

                IsGameOver = false;
                IsGameStarted = true;
                IsMyTurn = startInfo.FirstPlayer == playerName;
                MustDrawCard = IsMyTurn;

                if (IsMyTurn)
                {
                    Console.WriteLine($"DEBUG_CLIENT: Я первый игрок! Устанавливаю IsMyTurn = true, MustDrawCard = true");
                }

                onGameStarted?.Invoke(startInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки GameStarted: {ex.Message}");
                Console.WriteLine($"DEBUG_CLIENT: StackTrace: {ex.StackTrace}");
            }
        }

        private void HandleGameUpdate(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: !!! ПОЛУЧЕН НАСТОЯЩИЙ GAME_UPDATE !!!");

                var update = JsonSerializer.Deserialize<GameUpdate>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (update == null)
                {
                    Console.WriteLine($"DEBUG_CLIENT: Ошибка: update == null после десериализации");
                    return;
                }

                Console.WriteLine($"DEBUG_CLIENT: Десериализован GameUpdate (#{update.UpdateSequenceNumber})");
                Console.WriteLine($"DEBUG_CLIENT: CurrentPlayer: '{update.CurrentPlayer}'");
                Console.WriteLine($"DEBUG_CLIENT: IsMyTurn: {update.IsMyTurn}");
                Console.WriteLine($"DEBUG_CLIENT: MustDrawCard: {update.MustDrawCard}");

                // Проверка номера обновления
                long receivedUpdateNumber = update.UpdateSequenceNumber;
                lock (_updateNumberLock)
                {
                    if (receivedUpdateNumber <= _lastReceivedUpdateNumber)
                    {
                        Console.WriteLine($"DEBUG_CLIENT: ПОЛУЧЕНО УСТАРЕВШЕЕ обновление (#{receivedUpdateNumber} <= #{_lastReceivedUpdateNumber}). ИГНОРИРУЕТСЯ.");
                        return;
                    }
                    _lastReceivedUpdateNumber = receivedUpdateNumber;
                }

                Console.WriteLine($"DEBUG_CLIENT: Принято новое обновление (#{receivedUpdateNumber})");

                // Обновляем состояние клиента
                IsMyTurn = update.IsMyTurn;
                MustDrawCard = update.MustDrawCard;
                CanPlayNope = update.CanPlayNope;
                CanTakeFromDiscard = update.CanTakeFromDiscard;

                Console.WriteLine($"DEBUG_CLIENT: Установлено IsMyTurn: {IsMyTurn} для игрока {playerName}");
                Console.WriteLine($"DEBUG_CLIENT: MustDrawCard: {MustDrawCard}");

                // Обновляем состояние игры
                currentState = new GameState
                {
                    CurrentPlayer = update.CurrentPlayer ?? "",
                    DeckCount = update.DeckCount,
                    DiscardCount = update.DiscardPile?.Count ?? 0,
                    AlivePlayers = update.AlivePlayers ?? new List<string>(),
                    PlayerCardCounts = update.PlayerCardCounts ?? new Dictionary<string, int>(),
                    LastAction = update.LastAction ?? "",
                    NeedTarget = update.NeedTargetPlayer,
                    ActionType = update.ActionType ?? ""
                };

                // Синхронизация количества карт
                if (update.PlayerCardCounts != null && update.PlayerCardCounts.TryGetValue(playerName, out int serverHandCount))
                {
                    Console.WriteLine($"DEBUG_CLIENT: Сервер сообщает: у {playerName} должно быть {serverHandCount} карт");
                    Console.WriteLine($"DEBUG_CLIENT: У клиента сейчас {Hand.Count} карт в руке");

                    if (Hand.Count != serverHandCount)
                    {
                        Console.WriteLine($"DEBUG_CLIENT: ⚠️ ВНИМАНИЕ: расхождение в количестве карт!");
                        Console.WriteLine($"DEBUG_CLIENT: Клиент: {Hand.Count}, Сервер: {serverHandCount}");

                        if (Hand.Count > 0)
                        {
                            Console.WriteLine($"DEBUG_CLIENT: Текущие карты в руке клиента:");
                            for (int i = 0; i < Hand.Count; i++)
                            {
                                Console.WriteLine($"  [{i}] {GetCardDescription(Hand[i])} (ID: {Hand[i].Id})");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"DEBUG_CLIENT: ✅ Количество карт синхронизировано: {Hand.Count}");
                    }
                }

                // Устанавливаем флаг начала игры если еще не установлен
                if (!IsGameStarted && update.DeckCount > 0)
                {
                    IsGameStarted = true;
                    Console.WriteLine($"DEBUG_CLIENT: Игра началась! IsGameStarted = true");
                }

                onGameUpdate?.Invoke(update);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки GameUpdate: {ex.Message}");
                Console.WriteLine($"DEBUG_CLIENT: StackTrace: {ex.StackTrace}");
            }
        }

        private void HandleCardPlayed(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: Получен CARD_PLAYED, данные: {json}");

                var result = JsonSerializer.Deserialize<CardPlayResult>(json);
                if (result == null)
                {
                    Console.WriteLine($"DEBUG_CLIENT: Ошибка: result == null");
                    return;
                }

                Console.WriteLine($"DEBUG_CLIENT: Успешно десериализован CARD_PLAYED");
                Console.WriteLine($"DEBUG_CLIENT: Success: {result.Success}");
                Console.WriteLine($"DEBUG_CLIENT: Message: {result.Message}");

                if (result.Success)
                {
                    // Проверяем специальные типы карт
                    if (result.IsSeeTheFuture)
                    {
                        Console.WriteLine($"DEBUG_CLIENT: ⚠️ Это карта 'Заглянуть в будущее'");
                        if (result.CardsToAdd?.Any() == true)
                        {
                            Console.WriteLine($"DEBUG_CLIENT: Показать {result.CardsToAdd.Count} карт будущего:");
                            onShowFuture?.Invoke(result.CardsToAdd);
                        }
                    }
                    else if (result.WaitingForCardChoice)
                    {
                        Console.WriteLine($"DEBUG_CLIENT: ⏳ Ожидание выбора карты другим игроком");
                        // Ничего не делаем - ждем следующего пакета
                    }
                    else
                    {
                        // Обычные карты - добавляем в руку
                        AddCardsToHand(result);
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG_CLIENT: ❌ Ошибка: {result.Message}");
                }

                onCardPlayed?.Invoke(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки CARD_PLAYED: {ex.Message}");
                Console.WriteLine($"DEBUG_CLIENT: StackTrace: {ex.StackTrace}");
            }
        }

        private void AddCardsToHand(CardPlayResult result)
        {
            int addedCount = 0;

            // Добавляем карты из CardsToAdd
            if (result.CardsToAdd?.Any() == true)
            {
                foreach (var card in result.CardsToAdd)
                {
                    if (!Hand.Any(c => c.Id == card.Id))
                    {
                        Hand.Add(card);
                        addedCount++;
                        Console.WriteLine($"DEBUG_CLIENT: ✅ Добавлена карта: {card.Name} (ID: {card.Id})");
                    }
                }
            }

            // Добавляем DrawnCard если есть
            if (result.DrawnCard != null && !Hand.Any(c => c.Id == result.DrawnCard.Id))
            {
                Hand.Add(result.DrawnCard);
                addedCount++;
                Console.WriteLine($"DEBUG_CLIENT: ✅ Добавлен DrawnCard: {result.DrawnCard.Name} (ID: {result.DrawnCard.Id})");
            }

            Console.WriteLine($"DEBUG_CLIENT: Добавлено {addedCount} карт, итого в руке: {Hand.Count}");

            if (Hand.Count > 0)
            {
                Console.WriteLine($"DEBUG_CLIENT: Содержимое руки после обновления:");
                for (int i = 0; i < Hand.Count; i++)
                {
                    Console.WriteLine($"  [{i}] {GetCardDescription(Hand[i])} (ID: {Hand[i].Id})");
                }
            }
        }

        private void HandlePlayerEliminated(string json)
        {
            var eliminated = json.Trim('"');
            Console.WriteLine($"DEBUG_CLIENT: Игрок выбыл: {eliminated}");
            onPlayerEliminated?.Invoke(eliminated);
        }

        private void HandleGameOver(string json)
        {
            try
            {
                var gameOverInfo = JsonSerializer.Deserialize<dynamic>(json);
                IsGameOver = true;
                var winner = currentState.AlivePlayers.FirstOrDefault() ?? "Неизвестно";
                Console.WriteLine($"DEBUG_CLIENT: Игра окончена! Победитель: {winner}");
                onGameOver?.Invoke(winner);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки GAME_OVER: {ex.Message}");
            }
        }

        private void HandleNeedDefuse(string json)
        {
            var defuseRequest = json.Trim('"');
            Console.WriteLine($"DEBUG_CLIENT: Нужно обезвредить котёнка: {defuseRequest}");
            onNeedDefuse?.Invoke(defuseRequest);
        }

        private void HandleRequestTarget(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: Получен REQUEST_TARGET!");
                onNeedTarget?.Invoke(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки REQUEST_TARGET: {ex.Message}");
            }
        }

        private void HandleRequestCardSelection(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: Получен REQUEST_CARD_SELECTION!");
                HandleCardSelectionRequest(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки REQUEST_CARD_SELECTION: {ex.Message}");
            }
        }

        private async void HandleCardSelectionRequest(string json)
        {
            try
            {
                Console.WriteLine($"\n=== ЗАПРОС ВЫБОРА КАРТЫ ДЛЯ ПЕРЕДАЧИ ===");

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string message = root.TryGetProperty("Message", out JsonElement messageElement)
                    ? messageElement.GetString() ?? "Выберите карту для передачи"
                    : "Выберите карту для передачи";

                string initiator = root.TryGetProperty("Initiator", out JsonElement initiatorElement)
                    ? initiatorElement.GetString() ?? "неизвестный игрок"
                    : "неизвестный игрок";

                Console.WriteLine($"🎴 {message}");
                Console.WriteLine($"Игрок {initiator} просит у вас карту");

                // Получаем список доступных карт
                var availableCards = new List<(int Id, string Name)>();

                if (root.TryGetProperty("Cards", out JsonElement cardsElement))
                {
                    foreach (JsonElement card in cardsElement.EnumerateArray())
                    {
                        int id = card.TryGetProperty("Id", out JsonElement idElement)
                            ? idElement.GetInt32() : 0;
                        string name = card.TryGetProperty("Name", out JsonElement nameElement)
                            ? nameElement.GetString() ?? "Неизвестная карта" : "Неизвестная карта";

                        if (id > 0)
                        {
                            availableCards.Add((id, name));
                        }
                    }
                }

                if (!availableCards.Any())
                {
                    Console.WriteLine("У вас нет карт для передачи!");
                    return;
                }

                Console.WriteLine("\nВаши карты для передачи:");
                for (int i = 0; i < availableCards.Count; i++)
                {
                    Console.WriteLine($"[{i + 1}] {availableCards[i].Name}");
                }

                Console.Write("Выберите карту для передачи (номер): ");

                if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= availableCards.Count)
                {
                    int selectedCardId = availableCards[index - 1].Id;

                    // Отправляем выбор на сервер
                    var data = BitConverter.GetBytes(selectedCardId);
                    await SendPacket(ExplodingKittensProtocol.SEND_CARD_SELECTION, data);

                    Console.WriteLine($"Выбрана карта: {availableCards[index - 1].Name}");
                }
                else
                {
                    Console.WriteLine("Неверный выбор!");
                    // Автоматически выбираем первую карту
                    int selectedCardId = availableCards[0].Id;
                    var data = BitConverter.GetBytes(selectedCardId);
                    await SendPacket(ExplodingKittensProtocol.SEND_CARD_SELECTION, data);
                    Console.WriteLine($"Автоматически выбрана первая карта: {availableCards[0].Name}");
                }

                Console.WriteLine($"=== КОНЕЦ ЗАПРОСА ВЫБОРА КАРТЫ ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки запроса выбора карты: {ex.Message}");
            }
        }

        private void HandleShowFuture(string json)
        {
            try
            {
                var futureCards = JsonSerializer.Deserialize<List<Card>>(json);
                Console.WriteLine($"DEBUG_CLIENT: Показаны будущие карты: {futureCards?.Count ?? 0} карт");
                onShowFuture?.Invoke(futureCards ?? new List<Card>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки SHOW_FUTURE: {ex.Message}");
            }
        }

        private void HandleError(string json)
        {
            var error = json.Trim('"');
            Console.WriteLine($"DEBUG_CLIENT: Ошибка от сервера: {error}");
            onError?.Invoke(error);
        }

        private void HandleComboResult(string json)
        {
            try
            {
                Console.WriteLine($"DEBUG_CLIENT: Получен COMBO_RESULT, данные: {json}");
                var result = JsonSerializer.Deserialize<CardPlayResult>(json);
                if (result?.Success == true)
                {
                    AddCardsToHand(result);
                }
                onCardPlayed?.Invoke(result ?? new CardPlayResult());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка обработки COMBO_RESULT: {ex.Message}");
            }
        }

        private void HandleUnknownCommand(string command, string json)
        {
            Console.WriteLine($"DEBUG_CLIENT: Получена неизвестная команда: {command}");
            Console.WriteLine($"DEBUG_CLIENT: Данные: {json}");
        }

        #endregion

        private async Task SendPacket(string command, byte[]? data = null)
        {
            try
            {
                if (socket == null || !socket.Connected)
                {
                    Console.WriteLine($"DEBUG_CLIENT: Нет подключения для отправки пакета '{command}'");
                    return;
                }

                var packet = new Packet
                {
                    Command = command,
                    Data = data ?? Array.Empty<byte>()
                };

                var bytes = packet.ToBytes();
                Console.WriteLine($"DEBUG_CLIENT: Отправляю пакет '{command}', размер={bytes.Length}");
                await socket.SendAsync(bytes, SocketFlags.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_CLIENT: Ошибка отправки пакета '{command}': {ex.Message}");
            }
        }

        private string GetCardDescription(Card card)
        {
            return card.Type switch
            {
                CardType.ExplodingKitten => "💥 Взрывной котёнок",
                CardType.Defuse => "🛡️ Обезвредить",
                CardType.Attack => "⚔️ Атаковать",
                CardType.Skip => "⏭️ Пропустить",
                CardType.Favor => "🙏 Одолжить",
                CardType.Shuffle => "🔀 Перемешать",
                CardType.SeeTheFuture => "🔮 Заглянуть в будущее",
                CardType.Nope => "🚫 Нет",
                CardType.TacoCat => "🌮 Такокот",
                CardType.PotatoCat => "🥔 Картофелекот",
                CardType.BeardCat => "🧔 Бородакот",
                CardType.RainbowCat => "🌈 Радужнокот",
                CardType.CaticornCat => "🦄 Единорожекот",
                _ => $"❓ {card.Type}"
            };
        }

        // Метод для принудительной установки состояния для отладки
        public void SetDebugState(bool isMyTurn, bool mustDrawCard)
        {
            IsMyTurn = isMyTurn;
            MustDrawCard = mustDrawCard;
            Console.WriteLine($"DEBUG_CLIENT: Принудительно установлено: IsMyTurn={isMyTurn}, MustDrawCard={mustDrawCard}");
        }

        public void ForceHandUpdate(List<Card> newHand)
        {
            Console.WriteLine($"DEBUG_CLIENT: ForceHandUpdate: было {Hand.Count} карт");
            Hand = new List<Card>(newHand);
            Console.WriteLine($"DEBUG_CLIENT: Теперь {Hand.Count} карт");

            // Логируем
            for (int i = 0; i < Hand.Count; i++)
            {
                Console.WriteLine($"  [{i}] {GetCardDescription(Hand[i])}");
            }
        }
    }
}