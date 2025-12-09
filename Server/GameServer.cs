using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Common; 


namespace Server
{
    public class GameServer
    {
        private Socket serverSocket;
        private List<Player> connectedPlayers = new();
        private GameLogic gameLogic;
        private bool gameInProgress = false;
        private int requiredPlayers = 0;
        private Dictionary<string, int> playerRequests = new(); 


        public async Task Start()
        {
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 5001));
            serverSocket.Listen(10);

            Console.WriteLine("Сервер 'Взрывные котята' запущен на порту 5001");

            gameLogic = new GameLogic();

            while (true)
            {
                var clientSocket = await serverSocket.AcceptAsync();
                Console.WriteLine($"Новое подключение: {clientSocket.RemoteEndPoint}");

                _ = Task.Run(() => HandleClient(clientSocket));
            }
        }

        private async Task HandleClient(Socket socket)
        {
            Player player = null;

            try
            {
                player = new Player(socket);
                connectedPlayers.Add(player);

                var buffer = new byte[4096];

                while (socket.Connected)
                {
                    var received = await socket.ReceiveAsync(buffer, SocketFlags.None);
                    if (received == 0) break;

                    var packetData = new byte[received];
                    Array.Copy(buffer, packetData, received);

                    var packet = Packet.FromBytes(packetData);
                    await ProcessPacket(player, packet);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка с игроком {player?.Nickname}: {ex.Message}");
            }
            finally
            {
                if (player != null)
                {
                    connectedPlayers.Remove(player);
                    Console.WriteLine($"Игрок {player.Nickname} отключился");

                    // Если игра идёт и игрок выбыл
                    if (gameInProgress)
                    {
                        player.Eliminate();
                        await CheckGameEnd();
                    }
                }
                socket.Close();
            }
        }

        private async Task ProcessPacket(Player player, Packet packet)
        {
            try
            {
                switch (packet.Command)
                {
                    case ExplodingKittensProtocol.CONNECT:
                        await HandleConnect(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.START_GAME:
                        await HandleStartGame(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.PLAY_CARD:
                        await HandlePlayCard(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.DRAW_CARD:
                        await HandleDrawCard(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.PLAY_NOPE:
                        await HandlePlayNope(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.DEFUSE_KITTEN:
                        await HandleDefuseKitten(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.SELECT_TARGET:
                        await HandleSelectTarget(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.REQUEST_CARD:
                        await HandleRequestCard(player, packet.Data);
                        break;

                    case ExplodingKittensProtocol.TAKE_FROM_DISCARD:
                        await HandleTakeFromDiscard(player, packet.Data);
                        break;
                    case ExplodingKittensProtocol.REQUEST_CARD_CHOICE:
                        await HandleCardChoice(player, packet.Data);
                        break;
                    case ExplodingKittensProtocol.DISCONNECT:
                        player.Eliminate();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки пакета: {ex.Message}");
                SendError(player, ex.Message);
            }
        }

        // В GameServer.cs - улучшаем обработку подключения
        private async Task HandleConnect(Player player, byte[] data)
        {
            try
            {
                // ПРОВЕРЯЕМ ДАННЫЕ ПЕРЕД ДЕКОДИРОВАНИЕМ
                Console.WriteLine($"DEBUG: Получены байты для ника: {BitConverter.ToString(data)}");

                // Пытаемся декодировать как UTF-8
                var nickname = Encoding.UTF8.GetString(data);
                Console.WriteLine($"DEBUG: Декодирован ник: '{nickname}'");

                // Убираем лишние символы
                nickname = nickname.Trim('\0', ' ', '\t', '\n', '\r');
                Console.WriteLine($"DEBUG: После очистки: '{nickname}'");

                // Проверяем, не пустой ли ник
                if (string.IsNullOrWhiteSpace(nickname) || nickname.Length == 0)
                {
                    nickname = $"Игрок_{new Random().Next(1000, 9999)}";
                }

                // Проверяем, не занят ли уже такой ник
                if (connectedPlayers.Any(p => p.Nickname == nickname))
                {
                    nickname = $"{nickname}_{new Random().Next(1000, 9999)}";
                }

                player.Nickname = nickname;
                Console.WriteLine($"Игрок подключился: {player.Nickname}");

                // ОТМЕНЯЕМ отправку GAME_UPDATE при подключении!
                // Вместо этого отправляем простое подтверждение другой командой
                var response = new { Status = "CONNECTED", Players = connectedPlayers.Count, YourName = player.Nickname };
                var json = JsonSerializer.Serialize(response);
                var responseData = Encoding.UTF8.GetBytes(json);

                // Используем другую команду, например "CONNECTED"
                player.SendPacket("CONNECT_RESPONSE", responseData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки подключения: {ex.Message}");
                SendError(player, $"Ошибка подключения: {ex.Message}");
            }
        }


        // В методе HandleStartGame - улучшаем синхронизацию
        private async Task HandleStartGame(Player player, byte[] data)
        {
            if (gameInProgress)
            {
                SendError(player, "Игра уже началась");
                return;
            }

            int requestedPlayers = BitConverter.ToInt32(data, 0);
            playerRequests[player.Nickname] = requestedPlayers;

            // Проверяем, все ли запросили одинаковое количество
            var allRequests = playerRequests.Values.Distinct().ToList();
            var connectedCount = connectedPlayers.Count;

            if (allRequests.Count == 1 && connectedCount == requestedPlayers)
            {
                // Все согласны и все подключены
                requiredPlayers = requestedPlayers;
                gameInProgress = true;

                // Устанавливаем порядок игроков
                var orderedPlayers = connectedPlayers.ToList();
                gameLogic.InitializeGame(orderedPlayers);

                await BroadcastGameStart();
            }
            else
            {
                // Ждём остальных
                Console.WriteLine($"Игрок {player.Nickname} хочет играть с {requestedPlayers} игроками. Подключено: {connectedCount}/{requestedPlayers}");

                // Если уже есть нужное количество игроков с одинаковыми запросами
                var matchingRequests = playerRequests.Values.Where(v => v == requestedPlayers).Count();
                if (matchingRequests == requestedPlayers && connectedCount == requestedPlayers)
                {
                    requiredPlayers = requestedPlayers;
                    gameInProgress = true;
                    var orderedPlayers = connectedPlayers.ToList();
                    gameLogic.InitializeGame(orderedPlayers);
                    await BroadcastGameStart();
                }
            }
        }

        private async Task HandlePlayCard(Player player, byte[] data)
        {
            if (!gameInProgress)
            {
                SendError(player, "Игра ещё не началась");
                return;
            }

            var request = JsonSerializer.Deserialize<CardPlayRequest>(Encoding.UTF8.GetString(data));
            var result = await gameLogic.ProcessCardPlay(player, request);

            var response = JsonSerializer.Serialize(result);
            player.SendPacket(ExplodingKittensProtocol.CARD_PLAYED, Encoding.UTF8.GetBytes(response));
        }

        private async Task HandleDrawCard(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            int? placement = null;
            if (data.Length > 0)
            {
                placement = BitConverter.ToInt32(data, 0);
            }

            var result = await gameLogic.ProcessCardDraw(player, placement);

            var response = JsonSerializer.Serialize(result);
            player.SendPacket(ExplodingKittensProtocol.CARD_PLAYED, Encoding.UTF8.GetBytes(response));

            if (result.GameOver)
            {
                await BroadcastGameOver(result.Winner);
            }
        }

        private async Task HandlePlayNope(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            // Простая реализация "Нет"
            var nopeCard = new Card { Type = CardType.Nope };
            var request = new CardPlayRequest { Card = nopeCard };

            var result = await gameLogic.ProcessCardPlay(player, request);

            var response = JsonSerializer.Serialize(result);
            BroadcastToAll(ExplodingKittensProtocol.CARD_PLAYED, Encoding.UTF8.GetBytes(response));
        }

        private async Task HandleDefuseKitten(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            var placement = BitConverter.ToInt32(data, 0);
            var result = await gameLogic.ProcessDefuse(player, placement);

            var response = JsonSerializer.Serialize(result);
            player.SendPacket(ExplodingKittensProtocol.CARD_PLAYED, Encoding.UTF8.GetBytes(response));
        }

        private async Task HandleSelectTarget(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            var targetName = Encoding.UTF8.GetString(data);
            var result = await gameLogic.ProcessTargetSelection(player, targetName);

            var response = JsonSerializer.Serialize(result);
            player.SendPacket(ExplodingKittensProtocol.COMBO_RESULT, Encoding.UTF8.GetBytes(response));
        }

        private async Task HandleRequestCard(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            var request = Encoding.UTF8.GetString(data).Split('|');
            var targetName = request[0];
            var cardName = request.Length > 1 ? request[1] : "";

            var result = await gameLogic.ProcessTargetSelection(player, targetName, cardName);

            var response = JsonSerializer.Serialize(result);
            player.SendPacket(ExplodingKittensProtocol.COMBO_RESULT, Encoding.UTF8.GetBytes(response));
        }

        private async Task HandleTakeFromDiscard(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            // Для комбо из 5 разных - взять из сброса
            try
            {
                var card = gameLogic.Deck.TakeFromDiscard();
                player.AddCard(card);

                var result = new CardPlayResult
                {
                    Success = true,
                    Message = $"Вы взяли {card.Name} из сброса",
                    CardsToAdd = new List<Card> { card }
                };

                var response = JsonSerializer.Serialize(result);
                player.SendPacket(ExplodingKittensProtocol.COMBO_RESULT, Encoding.UTF8.GetBytes(response));

                await gameLogic.BroadcastGameUpdate();
            }
            catch (Exception ex)
            {
                SendError(player, ex.Message);
            }
        }

        // В GameServer.cs - улучшаем BroadcastGameStart
        private async Task BroadcastGameStart()
        {
            Console.WriteLine($"DEBUG: Начинаем рассылку GameStartInfo для {connectedPlayers.Count} игроков");

            foreach (var player in connectedPlayers)
            {
                var startInfo = new GameStartInfo
                {
                    PlayerHands = new Dictionary<string, List<Card>>(),
                    PlayersCount = connectedPlayers.Count,
                    FirstPlayer = gameLogic.GetCurrentPlayer()?.Nickname ?? "",
                    DefusesInDeck = 6 - connectedPlayers.Count
                };

                startInfo.PlayerHands[player.Nickname] = player.Hand.ToList();

                var json = JsonSerializer.Serialize(startInfo);
                var bytes = Encoding.UTF8.GetBytes(json);

                Console.WriteLine($"DEBUG: Отправляю GAME_STARTED игроку {player.Nickname}, размер={bytes.Length}, рука: {player.Hand.Count} карт");
                player.SendPacket(ExplodingKittensProtocol.GAME_STARTED, bytes);

                // Добавляем небольшую задержку между отправками
                await Task.Delay(100);
            }

            // В методе BroadcastGameStart() после отправки GAME_STARTED
            Console.WriteLine($"DEBUG_SERVER: Игра началась! Первый игрок: {gameLogic.GetCurrentPlayer()?.Nickname}");

            // Немедленно отправляем обновление состояния
            await Task.Delay(300);
            await gameLogic.BroadcastGameUpdate();
        }

        private async Task BroadcastGameOver(string winner)
        {
            var gameOverInfo = new { Winner = winner, Message = "Игра окончена!" };
            var json = JsonSerializer.Serialize(gameOverInfo);
            var bytes = Encoding.UTF8.GetBytes(json);

            BroadcastToAll(ExplodingKittensProtocol.GAME_OVER, bytes);

            gameInProgress = false;
            connectedPlayers.Clear();
        }

        private async Task CheckGameEnd()
        {
            var alivePlayers = connectedPlayers.Where(p => p.IsAlive).ToList();
            if (alivePlayers.Count == 1)
            {
                await BroadcastGameOver(alivePlayers[0].Nickname);
            }
        }

        private void BroadcastToAll(string command, byte[] data)
        {
            foreach (var player in connectedPlayers.Where(p => p.IsAlive))
            {
                player.SendPacket(command, data);
            }
        }

        private void SendError(Player player, string message)
        {
            var error = new { Error = message };
            var json = JsonSerializer.Serialize(error);
            player.SendPacket(ExplodingKittensProtocol.ERROR, Encoding.UTF8.GetBytes(json));
        }

        private async Task HandleCardChoice(Player player, byte[] data)
        {
            if (!gameInProgress) return;

            // Получаем ID выбранной карты
            int cardId = BitConverter.ToInt32(data, 0);

            var result = await gameLogic.ProcessCardChoice(player, cardId);

            var response = JsonSerializer.Serialize(result);
            player.SendPacket(ExplodingKittensProtocol.CARD_PLAYED, Encoding.UTF8.GetBytes(response));
        }
    }
}