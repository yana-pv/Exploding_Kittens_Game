using Common;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Client
{
    public class ConsoleGameApp
    {
        private GameClient gameClient;
        private bool isRunning = true;
        private string playerName = "";

        public async Task Run()
        {
            ShowWelcomeScreen();

            while (isRunning)
            {
                try
                {
                    var choice = ShowMainMenu();

                    switch (choice)
                    {
                        case "1":
                            await StartNewGame();
                            break;
                        case "2":
                            ShowRules();
                            break;
                        case "3":
                            isRunning = false;
                            break;
                        default:
                            Console.WriteLine("Неверный выбор!");
                            WaitForAnyKey();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    WaitForAnyKey();
                }
            }

            Console.WriteLine("До свидания!");
        }

        private void ShowWelcomeScreen()
        {
            Console.Clear();
            Console.WriteLine("==============================================");
            Console.WriteLine("            🐱 ВЗРЫВНЫЕ КОТЯТА 🐱           ");
            Console.WriteLine("               Консольная версия             ");
            Console.WriteLine("==============================================");
            Console.WriteLine();
        }

        private string ShowMainMenu()
        {
            Console.WriteLine("\nГЛАВНОЕ МЕНЮ:");
            Console.WriteLine("1. Начать новую игру");
            Console.WriteLine("2. Правила игры");
            Console.WriteLine("3. Выход");
            Console.Write("\nВыберите действие: ");

            return Console.ReadLine()?.Trim() ?? "";
        }

        private void ShowRules()
        {
            Console.Clear();
            Console.WriteLine("📚 ПРАВИЛА ИГРЫ:");
            Console.WriteLine("==============================================");
            Console.WriteLine("Цель: Не подорваться на взрывном котёнке!");
            Console.WriteLine();
            Console.WriteLine("Основные карты:");
            Console.WriteLine("💥 Взрывной котёнок - подрывает игрока");
            Console.WriteLine("🛡️  Обезвредить - спасает от котёнка");
            Console.WriteLine("⚔️  Атаковать - заставляет другого игрока взять 2 карты");
            Console.WriteLine("⏭️  Пропустить - пропускает ход");
            Console.WriteLine("🙏 Одолжить - заставляет отдать карту");
            Console.WriteLine("🔀 Перемешать - перемешивает колоду");
            Console.WriteLine("🔮 Заглянуть в будущее - показывает 3 верхние карты");
            Console.WriteLine("🚫 Нет - отменяет действие другого игрока");
            Console.WriteLine();
            Console.WriteLine("Котики (комбинируются 2, 3 или 5 разных):");
            Console.WriteLine("🌮 Такокот 🥔 Картофелекот 🧔 Бородакот");
            Console.WriteLine("🌈 Радужнокот 🦄 Единорожекот");
            Console.WriteLine("\nНажмите любую клавишу для возврата...");
            Console.ReadKey();
        }

        private async Task StartNewGame()
        {
            Console.Clear();
            Console.WriteLine("🎮 НОВАЯ ИГРА");
            Console.WriteLine("==============");

            var connectionInfo = GetConnectionInfo();

            try
            {
                gameClient = new GameClient();
                gameClient.SetupEventHandlers(
                    OnGameStarted, // Это сработает когда сервер начнет игру
                    OnGameUpdate,
                    OnCardPlayed,
                    OnPlayerEliminated,
                    OnGameOver,
                    OnNeedDefuse,
                    OnNeedTarget,
                    OnShowFuture,
                    OnError
                );

                Console.Write("Подключаемся к серверу... ");
                await gameClient.Connect(connectionInfo.Ip, connectionInfo.Nickname);
                Console.WriteLine("OK!");

                playerName = connectionInfo.Nickname;

                Console.Write("Запускаем игру... ");
                await gameClient.StartGame(connectionInfo.PlayerCount);
                Console.WriteLine("OK!");

                Console.WriteLine("Ожидание начала игры...");
                Console.WriteLine("(Нажмите Q чтобы выйти)");

                // Ждем начала игры (событие OnGameStarted)
                var startTask = WaitForGameStart();
                var quitTask = WaitForQuitKey();

                var completedTask = await Task.WhenAny(startTask, quitTask);

                if (completedTask == quitTask && quitTask.Result)
                {
                    gameClient.Disconnect();
                    return;
                }

                // Игра началась!
                await GameLoop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                WaitForAnyKey();
                gameClient?.Disconnect();
            }
        }

        private async Task<bool> WaitForGameStart()
        {
            while (!gameClient.IsGameStarted)
            {
                await Task.Delay(100);
            }
            return true;
        }

        private async Task<bool> WaitForQuitKey()
        {
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                        return true;
                }
                await Task.Delay(100);
            }
        }

        // В ConsoleGameApp.cs - временный фикс для ввода ника
        private (string Ip, string Nickname, int PlayerCount) GetConnectionInfo()
        {
            Console.Write("IP сервера [127.0.0.1]: ");
            var ip = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

            Console.Write("Ваш ник: ");

            // ВРЕМЕННОЕ РЕШЕНИЕ: читаем символы вручную
            var nicknameBuilder = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    nicknameBuilder.Append(key.KeyChar);
                }
                else if (key.Key == ConsoleKey.Backspace && nicknameBuilder.Length > 0)
                {
                    nicknameBuilder.Remove(nicknameBuilder.Length - 1, 1);
                    Console.Write("\b \b"); // Удаляем символ с экрана
                }
            }
            while (key.Key != ConsoleKey.Enter);

            var inputNickname = nicknameBuilder.ToString();
            Console.WriteLine(); // Переход на новую строку

            string nickname;
            if (string.IsNullOrWhiteSpace(inputNickname))
            {
                nickname = $"Игрок_{new Random().Next(1000, 9999)}";
            }
            else
            {
                nickname = inputNickname.Trim();
                if (nickname.Length > 20)
                    nickname = nickname.Substring(0, 20);
                if (string.IsNullOrWhiteSpace(nickname))
                    nickname = $"Игрок_{new Random().Next(1000, 9999)}";
            }

            Console.WriteLine($"DEBUG: Выбран ник: '{nickname}'");

            int players = 3;
            while (true)
            {
                Console.Write("Количество игроков (2-5) [3]: ");
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    break;

                if (int.TryParse(input, out players) && players >= 2 && players <= 5)
                    break;

                Console.WriteLine("Введите число от 2 до 5!");
            }

            return (ip, nickname, players);
        }

        // В ConsoleGameApp.cs - улучшаем GameLoop
        private async Task GameLoop()
        {
            Console.WriteLine("Начинаем игровой цикл...");

            // Ждем начала игры
            while (!gameClient.IsGameStarted && !gameClient.IsGameOver)
            {
                await Task.Delay(100);
            }

            if (gameClient.IsGameOver)
            {
                Console.WriteLine("Игра уже завершена!");
                WaitForAnyKey();
                return;
            }

            Console.WriteLine("Игра началась! Начинаем игровой цикл...");

            Console.WriteLine("Игра началась! Начинаем игровой цикл...");
            TestGameClientState();

            while (gameClient.IsConnected && !gameClient.IsGameOver)
            {
                try
                {
                    await ProcessGameInput();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в игровом цикле: {ex.Message}");
                }

                await Task.Delay(50);
            }

            Console.WriteLine("\nИгра завершена!");
            WaitForAnyKey();
            gameClient.Disconnect();
        }

        private async Task ProcessGameInput()
        {
            ShowGameState();

            // ВРЕМЕННО: Принудительно проверяем состояние
            if (!gameClient.IsGameStarted)
            {
                Console.WriteLine($"\nDEBUG: IsGameStarted = {gameClient.IsGameStarted}");
                Console.WriteLine("Ожидание начала игры...");
            }
            else if (gameClient.IsMyTurn)
            {
                Console.WriteLine($"\n=== ВАШ ХОД! (IsMyTurn = {gameClient.IsMyTurn}) ===");
                Console.WriteLine($"MustDrawCard: {gameClient.MustDrawCard}");
                await HandlePlayerTurn();
            }
            else
            {
                var state = gameClient.GetGameState();
                Console.WriteLine($"\nDEBUG: IsMyTurn = {gameClient.IsMyTurn}");
                Console.WriteLine($"DEBUG: CurrentPlayer в state: '{state?.CurrentPlayer}'");
                Console.WriteLine($"DEBUG: playerName: '{playerName}'");

                // ВРЕМЕННО: принудительно устанавливаем ход если CurrentPlayer совпадает
                if (state != null && state.CurrentPlayer == playerName && !gameClient.IsMyTurn)
                {
                    Console.WriteLine($"\n=== ОБНАРУЖЕНО РАСХОЖДЕНИЕ! ===");
                    Console.WriteLine("Принудительно устанавливаем IsMyTurn = true");
                    gameClient.SetDebugState(true, gameClient.MustDrawCard);
                    await HandlePlayerTurn();
                    return;
                }

                Console.WriteLine($"\nСейчас ходит: {(string.IsNullOrEmpty(state?.CurrentPlayer) ? "ожидание..." : state.CurrentPlayer)}");
                Console.Write("Нажмите Enter для обновления или Q для выхода: ");
                var input = Console.ReadLine()?.Trim().ToUpper();
                if (input == "Q")
                {
                    gameClient.Disconnect();
                    isRunning = false;
                }
            }
        }

        private void ShowGameState()
        {
            Console.Clear();
            Console.WriteLine($"🎮 ИГРА: {playerName}");
            Console.WriteLine("==============================================");

            var state = gameClient.GetGameState();
            if (state != null)
            {
                // Проверяем, есть ли данные о текущем игроке
                string currentPlayerDisplay = string.IsNullOrEmpty(state.CurrentPlayer)
                    ? "никто"
                    : state.CurrentPlayer;

                Console.WriteLine($"Ходит: {currentPlayerDisplay} {(state.CurrentPlayer == playerName ? "(ВЫ)" : "")}");
                Console.WriteLine($"Колода: {state.DeckCount} карт | Сброс: {state.DiscardCount} карт");

                // Отладочная информация
                Console.WriteLine($"DEBUG: IsMyTurn: {gameClient.IsMyTurn}, MustDrawCard: {gameClient.MustDrawCard}");

                if (state.AlivePlayers != null && state.AlivePlayers.Any())
                {
                    Console.WriteLine($"Живые игроки: {string.Join(", ", state.AlivePlayers)}");
                }
                else
                {
                    Console.WriteLine("Живые игроки: (список пуст или null)");
                }

                if (state.PlayerCardCounts != null && state.PlayerCardCounts.Any())
                {
                    Console.WriteLine("\nКарты у игроков:");
                    foreach (var kvp in state.PlayerCardCounts)
                    {
                        if (kvp.Key != playerName)
                        {
                            Console.WriteLine($"  {kvp.Key}: {kvp.Value} карт");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("\nНет данных о картах игроков");
                }

                if (!string.IsNullOrEmpty(state.LastAction))
                {
                    Console.WriteLine($"\n📢 {state.LastAction}");
                }
            }
            else
            {
                Console.WriteLine("Состояние игры недоступно (state == null)");
            }

            Console.WriteLine("\n==============================================");

            // Показываем руку
            gameClient.PrintHand();

            // Показываем доступные действия
            ShowAvailableActions();
        }

        private void ShowAvailableActions()
        {
            Console.WriteLine("\nДОСТУПНЫЕ ДЕЙСТВИЯ:");

            if (gameClient.IsMyTurn)
            {
                Console.WriteLine("=== ВАШ ХОД! ===");

                if (gameClient.MustDrawCard)
                {
                    Console.WriteLine("[D] - ⚠️ Взять карту из колоды (завершить ход)");
                    Console.WriteLine($"[1-{gameClient.HandCount}] - Выбрать карту для игры (если можно)");
                }
                else
                {
                    Console.WriteLine($"[1-{gameClient.HandCount}] - Выбрать карту для игры");
                    Console.WriteLine("[D] - Взять карту из колоды (пропустить ход без игры)");
                }

                Console.WriteLine("[N] - Сказать 'НЕТ!' (если есть карта)");

                if (gameClient.HandCount >= 2)
                {
                    Console.WriteLine("[C] - Сыграть комбо");
                }
            }
            else
            {
                Console.WriteLine("Сейчас не ваш ход");
                Console.WriteLine("[N] - Сказать 'НЕТ!' (если есть карта и возможность)");
            }

            Console.WriteLine("[S] - Обновить экран");
            Console.WriteLine("[H] - Показать руку");
            Console.WriteLine("[Q] - Сдаться и выйти");
            Console.WriteLine();
        }

        private async Task HandlePlayerTurn()
        {
            Console.WriteLine("\nВАШ ХОД!");
            Console.Write("Введите команду: ");

            var input = Console.ReadLine()?.Trim().ToUpper();

            switch (input)
            {
                case "D":
                    if (gameClient.MustDrawCard)
                    {
                        await gameClient.DrawCard();
                        Console.WriteLine("Карта вытянута!");
                        WaitForAnyKey();
                    }
                    else
                    {
                        Console.WriteLine("Сейчас не время тянуть карту!");
                        WaitForAnyKey();
                    }
                    break;

                case "N":
                    await gameClient.PlayNope();
                    Console.WriteLine("Сыграно 'НЕТ!'");
                    WaitForAnyKey();
                    break;

                case "C":
                    await PlayCombo();
                    break;

                case "H":
                case "S":
                    // Обновление экрана
                    break;

                case "Q":
                    gameClient.Disconnect();
                    isRunning = false;
                    break;

                default:
                    if (int.TryParse(input, out int cardIndex))
                    {
                        await gameClient.PlayCardByIndex(cardIndex - 1);
                        Console.WriteLine($"Карта {cardIndex} сыграна!");

                        // Проверяем, нужно ли выбрать цель
                        await CheckForTargetSelection();

                        WaitForAnyKey();
                    }
                    else
                    {
                        Console.WriteLine("Неизвестная команда! Нажмите любую клавишу...");
                        WaitForAnyKey();
                    }
                    break;
            }
        }

        private async Task CheckForTargetSelection()
        {
            // Ждем немного, чтобы сервер успел отправить запрос на выбор цели
            await Task.Delay(500);

            var state = gameClient.GetGameState();
            if (state.NeedTarget)
            {
                Console.WriteLine($"\nНужно выбрать цель!");

                var alivePlayers = gameClient.GetAlivePlayers().Where(p => p != playerName).ToList();
                if (alivePlayers.Any())
                {
                    Console.WriteLine("Доступные игроки:");
                    for (int i = 0; i < alivePlayers.Count; i++)
                    {
                        Console.WriteLine($"[{i + 1}] {alivePlayers[i]}");
                    }

                    Console.Write("Выберите игрока: ");
                    if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= alivePlayers.Count)
                    {
                        string requestedCard = "";
                        if (state.ActionType == "REQUEST_CARD")
                        {
                            Console.Write("Название карты: ");
                            requestedCard = Console.ReadLine() ?? "";
                        }

                        await gameClient.SelectTarget(alivePlayers[index - 1], requestedCard);
                        Console.WriteLine($"Выбрана цель: {alivePlayers[index - 1]}");
                    }
                }
            }
        }

        private async Task HandleNopeOpportunity()
        {
            Console.WriteLine("\n🚫 Вы можете сыграть карту 'НЕТ!' (10 секунд)...");
            Console.Write("Сыграть? [Y/N]: ");

            var timeoutTask = Task.Delay(10000);
            var inputTask = Task.Run(() => Console.ReadKey(true));

            var completedTask = await Task.WhenAny(timeoutTask, inputTask);

            if (completedTask == inputTask && inputTask.Result.KeyChar.ToString().ToUpper() == "Y")
            {
                await gameClient.PlayNope();
            }
        }

        private async Task PlayCombo()
        {
            Console.WriteLine("\n🎴 ИГРА КОМБО");
            Console.WriteLine("Выберите карты для комбо (номера через пробел):");
            Console.Write("> ");

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return;

            var indexes = input.Split(' ')
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .Select(i => i - 1)
                .Where(i => i >= 0 && i < gameClient.HandCount)
                .ToList();

            if (indexes.Count < 2)
            {
                Console.WriteLine("Нужно минимум 2 карты для комбо!");
                WaitForAnyKey();
                return;
            }

            // Первая карта - основная, остальные - комбо
            await gameClient.PlayComboByIndexes(indexes[0], indexes.Skip(1).ToList());
        }

        // ========== ОБРАБОТЧИКИ СОБЫТИЙ ==========

        // В ConsoleGameApp.cs - улучшаем OnGameStarted
        private void OnGameStarted(GameStartInfo startInfo)
        {
            Console.Clear();
            Console.WriteLine("🎉 ИГРА НАЧАЛАСЬ!");
            Console.WriteLine($"Первый ход: {startInfo.FirstPlayer}");
            Console.WriteLine($"Всего игроков: {startInfo.PlayersCount}");

            // Показываем руку конкретного игрока
            Console.WriteLine($"\n=== ВАША РУКА ({playerName}) ===");

            if (startInfo.PlayerHands.ContainsKey(playerName) && startInfo.PlayerHands[playerName] != null)
            {
                var playerHand = startInfo.PlayerHands[playerName];
                if (playerHand.Count > 0)
                {
                    for (int i = 0; i < playerHand.Count; i++)
                    {
                        var card = playerHand[i];
                        Console.WriteLine($"[{i + 1}] {GetCardName(card)}");
                    }
                }
                else
                {
                    Console.WriteLine("(пусто)");
                }
            }
            else
            {
                Console.WriteLine("Вашей руки не найдено в данных игры!");
                Console.WriteLine($"Доступные руки: {string.Join(", ", startInfo.PlayerHands.Keys)}");
            }

            Console.WriteLine("\nНажмите любую клавишу...");
            Console.ReadKey();
        }


        // В ConsoleGameApp.cs - улучшаем обработчики
        private void OnGameUpdate(GameUpdate update)
        {
            Console.WriteLine($"\n=== ПОЛУЧЕН OnGameUpdate ===");
            Console.WriteLine($"CurrentPlayer: '{update?.CurrentPlayer}'");
            Console.WriteLine($"IsMyTurn: {update?.IsMyTurn}");
            Console.WriteLine($"MustDrawCard: {update?.MustDrawCard}");
            Console.WriteLine($"DeckCount: {update?.DeckCount}");
            Console.WriteLine($"AlivePlayers: {string.Join(", ", update?.AlivePlayers ?? new List<string>())}");
            Console.WriteLine($"=== КОНЕЦ OnGameUpdate ===\n");

            WaitForAnyKey(); // Добавьте паузу чтобы увидеть сообщение
        }

        private void OnCardPlayed(CardPlayResult result)
        {
            Console.WriteLine($"\n=== РЕЗУЛЬТАТ КАРТЫ ===");
            Console.WriteLine(result.Message);

            if (result.Success)
            {
                Console.WriteLine("✅ Успешно!");

                if (result.DrawnCard != null)
                {
                    Console.WriteLine($"Получена карта: {GetCardName(result.DrawnCard)}");
                }

                if (result.CardsToAdd.Any())
                {
                    Console.WriteLine($"Получено карт: {result.CardsToAdd.Count}");
                    foreach (var card in result.CardsToAdd)
                    {
                        Console.WriteLine($"  - {GetCardName(card)}");
                    }
                }

                if (result.GameOver)
                {
                    Console.WriteLine($"\n🎮 ПОБЕДИТЕЛЬ: {result.Winner}!");
                }
            }
            else
            {
                Console.WriteLine($"❌ Ошибка: {result.Message}");
            }

            WaitForAnyKey();
        }

        private void OnPlayerEliminated(string eliminatedPlayer)
        {
            Console.WriteLine($"\n☠️ Игрок {eliminatedPlayer} выбыл из игры!");
            WaitForAnyKey();
        }

        private void OnGameOver(string winner)
        {
            Console.Clear();
            Console.WriteLine("==============================================");

            if (winner == playerName)
            {
                Console.WriteLine("             🎉 ВЫ ПОБЕДИЛИ! 🎉           ");
                Console.WriteLine("       Последний выживший котёнок!        ");
            }
            else
            {
                Console.WriteLine("             💀 ИГРА ОКОНЧЕНА 💀          ");
                Console.WriteLine($"          Победитель: {winner}           ");
            }

            Console.WriteLine("==============================================");
            WaitForAnyKey();
        }

        private void OnNeedDefuse(string request)
        {
            Console.WriteLine($"\n⚠️ {request}");
            Console.Write("Позиция для котёнка (1-вверху...5-внизу): ");

            if (int.TryParse(Console.ReadLine(), out int position) && position >= 1 && position <= 5)
            {
                gameClient.DefuseKitten(position);
            }
            else
            {
                Console.WriteLine("Неверная позиция! Выбирается случайно...");
                gameClient.DefuseKitten(new Random().Next(1, 6));
            }
        }

        private void OnNeedTarget(string requestJson)
        {
            try
            {
                Console.WriteLine($"\n=== ПОЛУЧЕН ЗАПРОС ВЫБОРА ЦЕЛИ ===");
                Console.WriteLine($"JSON: {requestJson}");

                // Используем JsonDocument для парсинга
                using JsonDocument doc = JsonDocument.Parse(requestJson);
                JsonElement root = doc.RootElement;

                string message = root.TryGetProperty("Message", out JsonElement messageElement)
                    ? messageElement.GetString() ?? "Выберите цель"
                    : "Выберите цель";

                Console.WriteLine($"🎯 {message}");

                var availableTargets = new List<string>();

                // Получаем список доступных целей из JSON
                if (root.TryGetProperty("AvailableTargets", out JsonElement targetsElement))
                {
                    foreach (JsonElement target in targetsElement.EnumerateArray())
                    {
                        string? targetName = target.GetString();
                        if (!string.IsNullOrEmpty(targetName))
                        {
                            availableTargets.Add(targetName);
                        }
                    }
                }

                if (availableTargets.Count == 0)
                {
                    // Если сервер не прислал список целей, берем из состояния
                    availableTargets = gameClient.GetAlivePlayers()
                        .Where(p => p != playerName)
                        .ToList();
                }

                if (!availableTargets.Any())
                {
                    Console.WriteLine("Нет других игроков!");
                    return;
                }

                Console.WriteLine("Доступные игроки:");
                for (int i = 0; i < availableTargets.Count; i++)
                {
                    Console.WriteLine($"[{i + 1}] {availableTargets[i]}");
                }

                Console.Write("Выберите игрока: ");
                if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= availableTargets.Count)
                {
                    string requestedCard = "";
                    if (message.Contains("Назовите") || message.Contains("назовите"))
                    {
                        Console.Write("Название карты: ");
                        requestedCard = Console.ReadLine() ?? "";
                    }

                    gameClient.SelectTarget(availableTargets[index - 1], requestedCard);
                    Console.WriteLine($"Выбрана цель: {availableTargets[index - 1]}");
                }
                else
                {
                    Console.WriteLine("Неверный выбор!");
                }

                Console.WriteLine($"=== КОНЕЦ ЗАПРОСА ЦЕЛИ ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки запроса цели: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private void OnShowFuture(List<Card> cards)
        {
            Console.Clear();
            Console.WriteLine("🔮 БУДУЩИЕ КАРТЫ:");
            Console.WriteLine("==================");

            for (int i = 0; i < cards.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {GetCardName(cards[i])}");
            }

            Console.WriteLine("\nНажмите любую клавишу...");
            Console.ReadKey();
        }

        private void OnError(string error)
        {
            Console.WriteLine($"\n⚠️ ОШИБКА: {error}");
            WaitForAnyKey();
        }

        // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

        private string GetCardName(Card card)
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

        private void WaitForAnyKey()
        {
            Console.WriteLine("\nНажмите любую клавишу...");
            Console.ReadKey(true);
        }

        // удалить
        private void TestGameClientState()
        {
            Console.WriteLine($"=== ТЕСТ СОСТОЯНИЯ КЛИЕНТА ===");
            Console.WriteLine($"playerName: {playerName}");
            Console.WriteLine($"IsGameStarted: {gameClient.IsGameStarted}");
            Console.WriteLine($"IsMyTurn: {gameClient.IsMyTurn}");
            Console.WriteLine($"MustDrawCard: {gameClient.MustDrawCard}");
            Console.WriteLine($"HandCount: {gameClient.HandCount}");
            Console.WriteLine($"IsConnected: {gameClient.IsConnected}");
            Console.WriteLine($"=== КОНЕЦ ТЕСТА ===");
            Console.WriteLine();
        }
    }
}