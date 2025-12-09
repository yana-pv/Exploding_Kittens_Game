using Common;
using System.Text;
using System.Text.Json;

namespace Server
{
    public class GameLogic
    {
        public Deck Deck { get; private set; }
        public List<Player> Players { get; private set; }
        public List<Player> AlivePlayers => Players.Where(p => p.IsAlive).ToList();

        private TurnProcessor turnProcessor;
        private Player currentPlayer;
        private bool gameStarted = false;
        private bool gameOver = false;
        private string winner = "";

        // Для отслеживания специальных состояний
        private bool waitingForTarget = false;
        private string pendingAction = "";
        private Player actionInitiator;

        // Для отслеживания Favor (игрок, у которого нужно выбрать карту)
        private Player favorTarget;
        private Player favorInitiator;

        // Счетчик обновлений
        private long _updateSequenceNumber = 0;
        private readonly object _updateLock = new object();

        public GameLogic()
        {
            Deck = new Deck();
            Players = new List<Player>();
        }

        public void InitializeGame(List<Player> players)
        {
            Players = players;
            Deck = new Deck();
            turnProcessor = new TurnProcessor(this, Deck, Players);

            Console.WriteLine($"DEBUG: Подготовка игры для {Players.Count} игроков");

            // Подготовка колоды
            var defuseCardsForPlayers = Deck.PrepareForGame(Players.Count);

            // Раздача начальных карт
            int playerIndex = 0;
            foreach (var player in Players)
            {
                player.Hand.Clear();
                player.IsAlive = true;
                player.HasDefuse = false;
                player.ResetTurnState();

                Console.WriteLine($"DEBUG: Раздача карт игроку {player.Nickname}");

                // 4 случайные карты
                for (int i = 0; i < 4; i++)
                {
                    if (Deck.Cards.Count == 0) break;
                    var card = Deck.Draw();
                    player.AddCard(card);
                }

                // 1 карта "Обезвредить" - берем из подготовленных
                if (playerIndex < defuseCardsForPlayers.Count)
                {
                    var defuseCard = defuseCardsForPlayers[playerIndex];
                    player.AddCard(defuseCard);
                }
                else
                {
                    // Резервная карта если не хватило
                    var defuseCard = new Card
                    {
                        Type = CardType.Defuse,
                        Category = CardCategory.Action,
                        Name = "Обезвредить",
                        Icon = "Defuse",
                        Id = 1000 + new Random().Next(1000)
                    };
                    player.AddCard(defuseCard);
                }

                playerIndex++;
            }

            // Устанавливаем текущего игрока через TurnProcessor
            if (turnProcessor != null)
            {
                turnProcessor.CurrentPlayer = Players[0];
                currentPlayer = turnProcessor.CurrentPlayer;
                Console.WriteLine($"DEBUG: Установлен текущий игрок: {currentPlayer.Nickname}");
            }

            gameStarted = true;
            Console.WriteLine($"Игра началась! Первый ход: {currentPlayer.Nickname}");
        }

        public async Task<CardPlayResult> ProcessCardPlay(Player player, CardPlayRequest request)
        {
            if (!gameStarted || gameOver)
                return new CardPlayResult { Success = false, Message = "Игра не активна" };

            if (player != currentPlayer && request.Card.Type != CardType.Nope)
                return new CardPlayResult { Success = false, Message = "Сейчас не ваш ход" };

            var result = await turnProcessor.ProcessTurn(player, request);

            if (result.Success)
            {
                Console.WriteLine($"DEBUG: Карта сыграна успешно: {result.Message}");

                // Если это карта "Заглянуть в будущее" - НЕ обновляем состояние
                if (request.Card.Type == CardType.SeeTheFuture)
                {
                    // Отправляем специальное сообщение с картами будущего
                    await SendFutureCards(player, result.CardsToAdd);
                    return result;
                }

                // Если нужно выбрать цель для Favor карты
                if (request.Card.Type == CardType.Favor)
                {
                    waitingForTarget = true;
                    pendingAction = "FAVOR";
                    actionInitiator = player;

                    Console.WriteLine($"DEBUG: Карта Favor сыграна, запрашиваем выбор цели у {player.Nickname}");
                    await SendTargetRequest(player, "Выберите игрока, у которого взять карту");
                }
                // Если нужно выбрать цель для комбо из 2 одинаковых
                else if (request.ComboCards != null && request.ComboCards.Count == 2)
                {
                    waitingForTarget = true;
                    pendingAction = "STEAL";
                    actionInitiator = player;

                    Console.WriteLine($"DEBUG: Комбо из 2 карт сыграно, запрашиваем выбор цели у {player.Nickname}");
                    await SendTargetRequest(player, "Выберите игрока, у которого взять случайную карту");
                }
                // Если нужно назвать карту для комбо из 3 одинаковых
                else if (request.ComboCards != null && request.ComboCards.Count == 3)
                {
                    waitingForTarget = true;
                    pendingAction = "REQUEST_CARD";
                    actionInitiator = player;

                    Console.WriteLine($"DEBUG: Комбо из 3 карт сыграно, запрашиваем выбор цели у {player.Nickname}");
                    await SendTargetRequest(player, "Назовите карту, которую хотите получить");
                }

                await BroadcastGameUpdate();
            }

            return result;
        }

        // Метод для отправки карт будущего игроку
        private async Task SendFutureCards(Player player, List<Card> futureCards)
        {
            try
            {
                var response = new CardPlayResult
                {
                    Success = true,
                    Message = "Вы видите будущее...",
                    CardsToAdd = futureCards,
                    IsSeeTheFuture = true  // ВАЖНО: добавляем флаг
                };

                var json = JsonSerializer.Serialize(response);
                var bytes = Encoding.UTF8.GetBytes(json);

                Console.WriteLine($"DEBUG_LOGIC: Отправка SHOW_FUTURE игроку {player.Nickname}");
                player.SendPacket(ExplodingKittensProtocol.SHOW_FUTURE, bytes);

                // Также отправляем CARD_PLAYED для общего обновления
                var playResult = new CardPlayResult
                {
                    Success = true,
                    Message = $"{player.Nickname} заглянул в будущее"
                };

                var playJson = JsonSerializer.Serialize(playResult);
                var playBytes = Encoding.UTF8.GetBytes(playJson);
                player.SendPacket(ExplodingKittensProtocol.CARD_PLAYED, playBytes);

                // После SeeTheFuture игрок должен завершить ход
                player.MustDrawCard = true;

                // Отправляем обновление состояния
                await BroadcastGameUpdate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_LOGIC: Ошибка отправки карт будущего: {ex.Message}");
            }
        }

        // Метод для отправки запроса выбора цели конкретному игроку
        private async Task SendTargetRequest(Player player, string message)
        {
            var request = new
            {
                Message = message,
                Action = pendingAction,
                AvailableTargets = Players.Where(p => p.IsAlive && p != player).Select(p => p.Nickname).ToList()
            };

            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);

            Console.WriteLine($"DEBUG: Отправка запроса выбора цели игроку {player.Nickname}: {message}");
            Console.WriteLine($"DEBUG: Доступные цели: {string.Join(", ", request.AvailableTargets)}");

            player.SendPacket(ExplodingKittensProtocol.REQUEST_TARGET, bytes);
        }

        public async Task<CardPlayResult> ProcessCardDraw(Player player, int? kittenPlacement = null)
        {
            Console.WriteLine($"DEBUG_LOGIC: ProcessCardDraw для {player.Nickname}");

            var result = await turnProcessor.ProcessCardDraw(player, kittenPlacement);

            Console.WriteLine($"DEBUG_LOGIC: После ProcessCardDraw: Success={result.Success}, Message={result.Message}");

            // Отправляем результат игроку
            if (player.Socket.Connected)
            {
                try
                {
                    var response = JsonSerializer.Serialize(result);
                    var bytes = Encoding.UTF8.GetBytes(response);

                    Console.WriteLine($"DEBUG_LOGIC: Отправка CARD_PLAYED игроку {player.Nickname}");
                    player.SendPacket(ExplodingKittensProtocol.CARD_PLAYED, bytes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DEBUG_LOGIC: Ошибка отправки CARD_PLAYED: {ex.Message}");
                }
            }

            return result;
        }

        public async Task<CardPlayResult> ProcessDefuse(Player player, int placement)
        {
            // Создаём фиктивного котёнка для обработки
            var kitten = new Card { Type = CardType.ExplodingKitten };
            var result = await turnProcessor.ProcessDefuse(player, kitten, placement);

            if (result.Success)
            {
                await BroadcastGameUpdate();
            }

            return result;
        }

        public async Task<CardPlayResult> ProcessTargetSelection(Player initiator, string targetName, string requestedCard = "")
        {
            var result = new CardPlayResult();
            var target = Players.FirstOrDefault(p => p.Nickname == targetName);

            Console.WriteLine($"DEBUG_LOGIC: ProcessTargetSelection: {initiator.Nickname} выбирает цель {targetName}, действие: {pendingAction}");

            if (target == null || !target.IsAlive || target == initiator)
            {
                result.Success = false;
                result.Message = "Неверная цель";
                return result;
            }

            if (!waitingForTarget || actionInitiator != initiator)
            {
                result.Success = false;
                result.Message = "Сейчас нельзя выбрать цель";
                return result;
            }

            if (pendingAction == "FAVOR")
            {
                return await ProcessFavorAction(initiator, target, result);
            }
            else if (pendingAction == "STEAL")
            {
                return await ProcessStealAction(initiator, target, result);
            }
            else if (pendingAction == "REQUEST_CARD")
            {
                return await ProcessRequestCardAction(initiator, target, requestedCard, result);
            }

            result.Success = false;
            result.Message = "Неизвестное действие";
            return result;
        }

        private async Task<CardPlayResult> ProcessFavorAction(Player initiator, Player target, CardPlayResult result)
        {
            if (target.Hand.Count == 0)
            {
                result.Success = false;
                result.Message = "У цели нет карт";
                ResetFavorState();
                return result;
            }

            if (target.Hand.Count == 1)
            {
                // Если у цели только одна карта - берем ее
                var cardToGive = target.Hand[0];
                target.RemoveCard(cardToGive);
                initiator.AddCard(cardToGive);

                result.Success = true;
                result.Message = $"{target.Nickname} дал карту {initiator.Nickname}";
                result.CardsToAdd = new List<Card> { cardToGive };

                // После Favor инициатор должен завершить ход взятием карты
                initiator.MustDrawCard = true;

                ResetFavorState();
                await BroadcastGameUpdate();
                return result;
            }

            // Если у цели несколько карт - нужно запросить у нее выбор
            favorTarget = target;
            favorInitiator = initiator;

            // Отправляем запрос выбора карты цели
            await SendCardChoiceRequest(target, initiator);

            result.Success = true;
            result.Message = $"Ожидание выбора карты от {target.Nickname}";
            result.WaitingForCardChoice = true;

            // НЕ сбрасываем состояние - ждем выбора карты
            Console.WriteLine($"DEBUG_LOGIC: Запрошен выбор карты у {target.Nickname}");
            return result;
        }

        private async Task<CardPlayResult> ProcessStealAction(Player initiator, Player target, CardPlayResult result)
        {
            if (target.Hand.Count == 0)
            {
                result.Success = false;
                result.Message = "У цели нет карт";
                ResetFavorState();
                return result;
            }

            var random = new Random();
            var stolenCard = target.Hand[random.Next(target.Hand.Count)];

            target.RemoveCard(stolenCard);
            initiator.AddCard(stolenCard);

            result.Success = true;
            result.Message = $"{initiator.Nickname} украл карту у {target.Nickname}";
            result.CardsToAdd = new List<Card> { stolenCard };

            // После кражи игрок должен завершить ход
            initiator.MustDrawCard = true;

            ResetFavorState();
            await BroadcastGameUpdate();
            return result;
        }

        private async Task<CardPlayResult> ProcessRequestCardAction(Player initiator, Player target, string requestedCard, CardPlayResult result)
        {
            if (string.IsNullOrWhiteSpace(requestedCard))
            {
                result.Success = false;
                result.Message = "Не указана карта для запроса";
                ResetFavorState();
                return result;
            }

            var requested = target.Hand.FirstOrDefault(c => c.Name.Contains(requestedCard, StringComparison.OrdinalIgnoreCase));
            if (requested != null)
            {
                target.RemoveCard(requested);
                initiator.AddCard(requested);

                result.Success = true;
                result.Message = $"{initiator.Nickname} получил карту '{requestedCard}' от {target.Nickname}";
                result.CardsToAdd = new List<Card> { requested };

                // После получения карты игрок должен завершить ход
                initiator.MustDrawCard = true;

                ResetFavorState();
                await BroadcastGameUpdate();
            }
            else
            {
                result.Success = false;
                result.Message = $"У {target.Nickname} нет карты '{requestedCard}'";
                ResetFavorState();
            }

            return result;
        }

        private void ResetFavorState()
        {
            waitingForTarget = false;
            pendingAction = "";
            actionInitiator = null;
            favorTarget = null;
            favorInitiator = null;
        }

        // Метод для отправки запроса выбора карты цели Favor
        private async Task SendCardChoiceRequest(Player target, Player initiator)
        {
            try
            {
                var request = new
                {
                    Message = $"Игрок {initiator.Nickname} просит у вас карту. Выберите карту для передачи:",
                    Action = "FAVOR_CHOICE",
                    Initiator = initiator.Nickname,
                    Cards = target.Hand.Select(c => new
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Type = c.Type.ToString()
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(request);
                var bytes = Encoding.UTF8.GetBytes(json);

                Console.WriteLine($"DEBUG_LOGIC: Отправляем запрос выбора карты игроку {target.Nickname}");
                target.SendPacket(ExplodingKittensProtocol.REQUEST_TARGET, bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_LOGIC: Ошибка отправки запроса выбора карты: {ex.Message}");
            }
        }

        // Новый метод для обработки выбора карты в Favor
        public async Task<CardPlayResult> ProcessCardChoice(Player target, int cardId, string cardName = "")
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_LOGIC: ProcessCardChoice: {target.Nickname}, cardId: {cardId}, cardName: {cardName}");

            // Проверяем, есть ли ожидание выбора карты для Favor
            if (favorTarget == null || favorInitiator == null || favorTarget != target)
            {
                result.Success = false;
                result.Message = "Нельзя выбрать карту сейчас";
                return result;
            }

            // Находим карту
            Card cardToGive = null;
            if (cardId > 0)
            {
                cardToGive = target.Hand.FirstOrDefault(c => c.Id == cardId);
            }
            else if (!string.IsNullOrEmpty(cardName))
            {
                cardToGive = target.Hand.FirstOrDefault(c => c.Name.Contains(cardName, StringComparison.OrdinalIgnoreCase));
            }

            if (cardToGive == null)
            {
                result.Success = false;
                result.Message = "Карта не найдена";
                return result;
            }

            // Передаем карту
            target.RemoveCard(cardToGive);
            favorInitiator.AddCard(cardToGive);

            result.Success = true;
            result.Message = $"{target.Nickname} дал карту {favorInitiator.Nickname}";
            result.CardsToAdd = new List<Card> { cardToGive };

            // После Favor инициатор должен завершить ход взятием карты
            favorInitiator.MustDrawCard = true;

            // Сбрасываем состояние Favor
            ResetFavorState();

            Console.WriteLine($"DEBUG_LOGIC: Favor завершен. {favorInitiator?.Nickname} должен тянуть карту");

            // Отправляем обновление
            await BroadcastGameUpdate();

            return result;
        }

        public async Task BroadcastGameUpdate()
        {
            // Получаем и увеличиваем номер обновления
            long updateNumber;
            lock (_updateLock)
            {
                _updateSequenceNumber++;
                updateNumber = _updateSequenceNumber;
            }

            // Берем текущего игрока из TurnProcessor
            if (turnProcessor != null)
            {
                currentPlayer = turnProcessor.CurrentPlayer;
            }

            Console.WriteLine($"DEBUG_LOGIC: BroadcastGameUpdate (#{updateNumber}) - Текущий: {currentPlayer?.Nickname}");

            var update = new GameUpdate
            {
                CurrentPlayer = currentPlayer?.Nickname ?? "",
                DeckCount = Deck.Cards.Count,
                DiscardPile = Deck.DiscardPile.ToList(),
                LastAction = "",
                CanPlayNope = true,
                MustDrawCard = currentPlayer?.MustDrawCard ?? false,
                IsMyTurn = false,
                AlivePlayers = AlivePlayers.Select(p => p.Nickname).ToList(),
                PlayerCardCounts = Players.ToDictionary(p => p.Nickname, p => p.Hand.Count),
                NeedTargetPlayer = waitingForTarget || (favorTarget != null),
                ActionType = pendingAction,
                UpdateSequenceNumber = updateNumber
            };

            var json = JsonSerializer.Serialize(update);
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var player in Players.Where(p => p.IsAlive))
            {
                var playerUpdate = new GameUpdate
                {
                    CurrentPlayer = update.CurrentPlayer,
                    DeckCount = update.DeckCount,
                    DiscardPile = update.DiscardPile,
                    LastAction = update.LastAction,
                    CanPlayNope = update.CanPlayNope,
                    MustDrawCard = update.MustDrawCard,
                    IsMyTurn = (player.Nickname == update.CurrentPlayer),
                    AlivePlayers = update.AlivePlayers,
                    PlayerCardCounts = update.PlayerCardCounts,
                    NeedTargetPlayer = ((player == actionInitiator && waitingForTarget) || (player == favorTarget)),
                    ActionType = update.ActionType,
                    UpdateSequenceNumber = updateNumber
                };

                var playerJson = JsonSerializer.Serialize(playerUpdate);
                var playerBytes = Encoding.UTF8.GetBytes(playerJson);

                Console.WriteLine($"DEBUG_LOGIC: Отправка игроку {player.Nickname} - IsMyTurn: {playerUpdate.IsMyTurn}");

                player.SendPacket(ExplodingKittensProtocol.GAME_UPDATE, playerBytes);
            }
        }

        public Player GetPlayerByName(string name) =>
            Players.FirstOrDefault(p => p.Nickname == name);

        public Player GetCurrentPlayer() => currentPlayer;

        public bool IsGameOver() => gameOver;
        public string GetWinner() => winner;
    }
}