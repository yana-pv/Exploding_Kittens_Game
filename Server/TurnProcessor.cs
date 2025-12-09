using Common;
using System.Text;
using System.Text.Json;

namespace Server
{
    public class TurnProcessor
    {
        private readonly GameLogic gameLogic;
        private readonly Deck deck;
        private readonly List<Player> players;

        // Состояние текущего хода
        private Card lastPlayedCard;
        private List<Card> currentCombo = new();
        private int nopeCount = 0;
        private bool isNoped = false;
        private Stack<Card> nopeStack = new();

        // Текущий игрок
        private Player _currentPlayer;

        // Для отслеживания Favor
        private Player pendingFavorTarget;
        private Player pendingFavorInitiator;
        private bool waitingForCardChoice = false;
        public Player CurrentPlayer
        {
            get => _currentPlayer;
            set
            {
                if (_currentPlayer != null && _currentPlayer != value)
                {
                    Console.WriteLine($"DEBUG_TURN: Смена игрока {_currentPlayer.Nickname} -> {value?.Nickname}");

                    // Сбрасываем состояние предыдущего игрока
                    _currentPlayer.IsCurrentTurn = false;
                    _currentPlayer.DrewCardThisTurn = false;
                    _currentPlayer.PlayedCardThisTurn = false;
                    _currentPlayer.MustDrawCard = false;

                    if (_currentPlayer.AttackTurnsRemaining > 0)
                    {
                        Console.WriteLine($"DEBUG_TURN: Сбрасываем атаки у {_currentPlayer.Nickname}");
                        _currentPlayer.AttackTurnsRemaining = 0;
                    }
                }

                _currentPlayer = value;

                if (_currentPlayer != null)
                {
                    _currentPlayer.IsCurrentTurn = true;

                    // В начале хода игрок может либо играть карты, либо сразу тянуть карту
                    _currentPlayer.MustDrawCard = true; // Может сразу завершить ход взятием карты
                    _currentPlayer.DrewCardThisTurn = false;
                    _currentPlayer.PlayedCardThisTurn = false;

                    Console.WriteLine($"DEBUG_TURN: Текущий игрок установлен: {_currentPlayer.Nickname}");
                    Console.WriteLine($"DEBUG_TURN: MustDrawCard={_currentPlayer.MustDrawCard}, AttackTurns={_currentPlayer.AttackTurnsRemaining}");
                }
            }
        }

        // Флаг ожидания выбора цели
        public bool WaitingForTarget { get; private set; }
        public string PendingAction { get; private set; } = "";
        public Player ActionInitiator { get; private set; }

        public TurnProcessor(GameLogic logic, Deck gameDeck, List<Player> gamePlayers)
        {
            gameLogic = logic;
            deck = gameDeck;
            players = gamePlayers;

            if (players.Count > 0)
            {
                CurrentPlayer = players[0];
                Console.WriteLine($"DEBUG: TurnProcessor создан, первый игрок: {CurrentPlayer.Nickname}");
            }
        }

        /// <summary>
        /// Основной метод обработки хода
        /// </summary>
        public async Task<CardPlayResult> ProcessTurn(Player player, CardPlayRequest request)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: ProcessTurn: {player.Nickname}, карта: {request.Card.Type}");

            // 1. Проверка возможности игры карты
            if (!CanPlayCardNow(player, request.Card, request.ComboCards))
            {
                result.Success = false;
                result.Message = "Нельзя сыграть эту карту сейчас";
                return result;
            }

            // 2. Обработка "НЕТ"
            if (request.Card.Type == CardType.Nope)
            {
                return ProcessNopeCard(player, request);
            }

            // 3. Проверка комбо
            if (request.ComboCards?.Any() == true)
            {
                if (!ValidateCombo(request.ComboCards))
                {
                    result.Success = false;
                    result.Message = "Неверная комбинация карт";
                    return result;
                }
                return await ProcessCombo(player, request);
            }

            // 4. Обработка одиночной карты
            return await ProcessSingleCard(player, request);
        }

        private bool CanPlayCardNow(Player player, Card card, List<Card> combo)
        {
            Console.WriteLine($"DEBUG_TURN: CanPlayCardNow проверка для {player.Nickname}, карта: {card.Type}");

            // Карта "Нет" может быть сыграна в любое время
            if (card.Type == CardType.Nope)
            {
                if (lastPlayedCard != null &&
                   (lastPlayedCard.Type == CardType.Defuse ||
                    lastPlayedCard.Type == CardType.ExplodingKitten))
                {
                    return false;
                }
                return true;
            }

            // Для остальных карт - проверяем, что это текущий игрок
            if (player != CurrentPlayer)
            {
                return false;
            }

            // Если игрок уже взял карту в этом ходу, он не может играть другие карты
            if (player.DrewCardThisTurn)
            {
                Console.WriteLine($"DEBUG_TURN: Игрок уже взял карту в этом ходу");
                return false;
            }

            // Проверка наличия карты в руке
            if (!player.Hand.Contains(card))
            {
                return false;
            }

            // Для комбо - проверка всех карт
            if (combo?.Any() == true && !combo.All(c => player.Hand.Contains(c)))
            {
                return false;
            }

            return true;
        }

        private bool ValidateCombo(List<Card> combo)
        {
            if (combo.Count == 2)
            {
                return combo[0].Icon == combo[1].Icon;
            }
            else if (combo.Count == 3)
            {
                return combo[0].Icon == combo[1].Icon &&
                       combo[1].Icon == combo[2].Icon;
            }
            else if (combo.Count == 5)
            {
                var icons = combo.Select(c => c.Icon).Distinct();
                return icons.Count() == 5;
            }
            return false;
        }

        private async Task<CardPlayResult> ProcessSingleCard(Player player, CardPlayRequest request)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: Processing single card: {request.Card.Type} для {player.Nickname}");

            // Убираем карту из руки
            player.RemoveCard(request.Card);
            deck.Discard(request.Card);
            lastPlayedCard = request.Card;
            player.PlayedCardThisTurn = true;

            // Обработка эффекта карты
            switch (request.Card.Type)
            {
                case CardType.Attack:
                    result = await ProcessAttackCard(player);
                    break;

                case CardType.Skip:
                    result = await ProcessSkipCard(player);
                    break;

                case CardType.Favor:
                    result.Success = true;
                    result.Message = "Выберите игрока, у которого взять карту";

                    // Устанавливаем флаг ожидания выбора цели
                    WaitingForTarget = true;
                    PendingAction = "FAVOR";
                    ActionInitiator = player;

                    // После выбора цели нужно будет выбрать карту у цели
                    player.MustDrawCard = false; // Ждем выбора цели и карты
                    Console.WriteLine($"DEBUG_TURN: Favor карта сыграна, ожидаем выбор цели");
                    break;

                case CardType.Shuffle:
                    deck.Shuffle();
                    result.Success = true;
                    result.Message = $"{player.Nickname} перемешал колоду!";

                    // После перемешивания игрок может продолжить играть карты
                    player.MustDrawCard = true; // Может завершить ход
                    Console.WriteLine($"DEBUG_TURN: После перемешивания игрок может завершить ход");
                    break;

                case CardType.SeeTheFuture:
                    var futureCards = deck.PeekTopCards(3);
                    result.Success = true;
                    result.Message = "Вы видите будущее...";
                    result.CardsToAdd = futureCards;
                    result.IsSeeTheFuture = true; // ВАЖНО: устанавливаем флаг

                    player.MustDrawCard = true;

                    Console.WriteLine($"DEBUG_TURN: SeeTheFuture: показано {futureCards.Count} карт (остаются в колоде)");
                    break;

                case CardType.TacoCat:
                case CardType.PotatoCat:
                case CardType.BeardCat:
                case CardType.RainbowCat:
                case CardType.CaticornCat:
                    // Карты котиков без комбо - просто сбрасываем
                    result.Success = true;
                    result.Message = $"{player.Nickname} сыграл карту котика";
                    player.MustDrawCard = true; // Может завершить ход
                    break;

                default:
                    // Обычные карты действий
                    player.MustDrawCard = true;
                    result.Success = true;
                    result.Message = $"{player.Nickname} сыграл карту";
                    break;
            }

            return result;
        }

        private async Task<CardPlayResult> ProcessAttackCard(Player player)
        {
            var result = new CardPlayResult { Success = true };

            Console.WriteLine($"DEBUG_TURN: Processing Attack card от {player.Nickname}");

            // Находим следующего живого игрока
            var nextPlayer = GetNextAlivePlayer(player);
            if (nextPlayer == null)
            {
                result.Message = "Нет следующего игрока!";
                return result;
            }

            // Атака заставляет следующего игрока ходить 2 раза
            nextPlayer.AttackTurnsRemaining = 2;
            Console.WriteLine($"DEBUG_TURN: Атака! {nextPlayer.Nickname} будет ходить {nextPlayer.AttackTurnsRemaining} раза");

            result.Message = $"{player.Nickname} атаковал! {nextPlayer.Nickname} ходит 2 раза подряд";

            // Атака заканчивает ход текущего игрока
            player.MustDrawCard = false;

            // Передаём ход атакованному игроку
            CurrentPlayer = nextPlayer;

            // Отправляем обновление игры
            await gameLogic.BroadcastGameUpdate();

            return result;
        }

        private async Task<CardPlayResult> ProcessSkipCard(Player player)
        {
            var result = new CardPlayResult { Success = true };

            Console.WriteLine($"DEBUG_TURN: Processing Skip card для {player.Nickname}, AttackTurns={player.AttackTurnsRemaining}");

            // Если игрок под атакой
            if (player.AttackTurnsRemaining > 0)
            {
                player.AttackTurnsRemaining--;
                result.Message = $"{player.Nickname} пропускает один ход из атаки";
                Console.WriteLine($"DEBUG_TURN: Пропуск под атакой, осталось ходов: {player.AttackTurnsRemaining}");

                if (player.AttackTurnsRemaining == 0)
                {
                    // Атака закончилась, передаём ход следующему
                    Console.WriteLine($"DEBUG_TURN: Атака закончилась, передаём ход следующему");
                    var nextPlayer = GetNextAlivePlayer(player);
                    if (nextPlayer != null)
                    {
                        CurrentPlayer = nextPlayer;
                    }
                }
                else
                {
                    // Ещё остались ходы атаки, игрок продолжает ходить
                    Console.WriteLine($"DEBUG_TURN: Остались ходы атаки, игрок продолжает ход");
                    CurrentPlayer = player;
                }
            }
            else
            {
                // Обычный пропуск
                result.Message = $"{player.Nickname} пропускает ход";
                Console.WriteLine($"DEBUG_TURN: Обычный пропуск, передаём ход следующему");

                // Передаём ход следующему игроку
                var nextPlayer = GetNextAlivePlayer(player);
                if (nextPlayer != null)
                {
                    CurrentPlayer = nextPlayer;
                }
            }

            // Пропуск заканчивает ход
            player.MustDrawCard = false;

            // Отправляем обновление игры
            await gameLogic.BroadcastGameUpdate();

            return result;
        }

        private async Task<CardPlayResult> ProcessCombo(Player player, CardPlayRequest request)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: Processing combo of {request.ComboCards.Count} cards");

            // Убираем карты из руки
            foreach (var card in request.ComboCards)
            {
                player.RemoveCard(card);
                deck.Discard(card);
            }

            player.PlayedCardThisTurn = true;

            // Обработка в зависимости от типа комбо
            if (request.ComboCards.Count == 2)
            {
                result.Success = true;
                result.Message = "Выберите игрока, у которого взять случайную карту";

                // Устанавливаем флаг ожидания цели
                WaitingForTarget = true;
                PendingAction = "STEAL";
                ActionInitiator = player;

                // Ждем выбора цели
                player.MustDrawCard = false;
                Console.WriteLine($"DEBUG_TURN: Комбо 2 одинаковых - ожидаем выбор цели");
            }
            else if (request.ComboCards.Count == 3)
            {
                result.Success = true;
                result.Message = "Назовите карту, которую хотите получить";

                // Устанавливаем флаг ожидания цели
                WaitingForTarget = true;
                PendingAction = "REQUEST_CARD";
                ActionInitiator = player;

                // Ждем выбора цели и карты
                player.MustDrawCard = false;
                Console.WriteLine($"DEBUG_TURN: Комбо 3 одинаковых - ожидаем выбор цели и карты");
            }
            else if (request.ComboCards.Count == 5)
            {
                result.Success = true;
                result.Message = "Комбо из 5 разных карт! Используйте кнопку 'Взять из сброса'.";

                // После комбо из 5 игрок может взять карту из сброса
                player.MustDrawCard = true;
                Console.WriteLine($"DEBUG_TURN: Комбо 5 разных - можно взять из сброса");
            }

            // После комбо (кроме комбо из 5) игрок должен завершить ход взятием карты
            // Но сначала нужно выбрать цель для комбо 2 и 3
            if (request.ComboCards.Count == 5)
            {
                player.MustDrawCard = true;
            }

            return result;
        }

        private CardPlayResult ProcessNopeCard(Player player, CardPlayRequest request)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: Processing Nope card от {player.Nickname}");

            if (lastPlayedCard == null)
            {
                result.Success = false;
                result.Message = "Нечего отменять";
                return result;
            }

            // Убираем карту "Нет" из руки
            player.RemoveCard(request.Card);
            deck.Discard(request.Card);

            nopeCount++;

            if (nopeStack.Count > 0 && nopeStack.Peek().Type == CardType.Nope)
            {
                // "Нет" на "Нет" - отменяем предыдущий "Нет"
                nopeStack.Pop();
                nopeCount--;

                if (nopeStack.Count == 0)
                {
                    // Все "Нет" отменены, действие проходит
                    isNoped = false;
                    result.Message = $"{player.Nickname} отменил 'Нет'! Действие проходит.";
                }
                else
                {
                    result.Message = $"{player.Nickname} сыграл 'Нет' на 'Нет'";
                }
            }
            else
            {
                // Обычный "Нет" - кладём в стек
                nopeStack.Push(request.Card);
                isNoped = true;
                result.Message = $"{player.Nickname} сказал 'Нет'! Действие отменено.";
            }

            result.Success = true;
            return result;
        }

        public async Task<CardPlayResult> ProcessCardDraw(Player player, int? kittenPlacement = null)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: ProcessCardDraw для {player.Nickname}");
            Console.WriteLine($"DEBUG_TURN: MustDrawCard={player.MustDrawCard}, DrewCardThisTurn={player.DrewCardThisTurn}, IsCurrentTurn={player.IsCurrentTurn}");
            Console.WriteLine($"DEBUG_TURN: Карт в руке до: {player.Hand.Count}");

            // Проверяем, может ли игрок тянуть карту
            if (!player.IsCurrentTurn)
            {
                result.Success = false;
                result.Message = "Сейчас не ваш ход";
                return result;
            }

            if (player.DrewCardThisTurn)
            {
                result.Success = false;
                result.Message = "Вы уже взяли карту в этом ходу";
                return result;
            }

            // Тянем карту
            Card drawnCard;
            if (kittenPlacement.HasValue && kittenPlacement == -1)
            {
                drawnCard = deck.DrawFromBottom();
            }
            else if (kittenPlacement.HasValue)
            {
                drawnCard = deck.DrawAtPosition(kittenPlacement.Value);
            }
            else
            {
                drawnCard = deck.Draw();
            }

            Console.WriteLine($"DEBUG_TURN: Вытянута карта: {drawnCard.Type} ({drawnCard.Name}), ID: {drawnCard.Id}");

            player.DrewCardThisTurn = true;
            player.MustDrawCard = false;
            player.PlayedCardThisTurn = true;

            // Проверяем, не котёнок ли
            if (drawnCard.Type == CardType.ExplodingKitten)
            {
                return await ProcessKittenDraw(player, drawnCard);
            }

            // Обычная карта
            player.AddCard(drawnCard);
            Console.WriteLine($"DEBUG_TURN: Карта добавлена в руку. Теперь карт в руке: {player.Hand.Count}");

            result.Success = true;
            result.DrawnCard = drawnCard;
            result.Message = $"{player.Nickname} вытянул {drawnCard.Name}";

            // ВАЖНО: Убедимся, что DrawnCard имеет все необходимые поля
            if (result.DrawnCard != null)
            {
                Console.WriteLine($"DEBUG_TURN: DrawnCard установлен: Name={result.DrawnCard.Name}, Type={result.DrawnCard.Type}, Id={result.DrawnCard.Id}");
            }

            // Также добавляем в CardsToAdd для совместимости
            result.CardsToAdd = new List<Card> { drawnCard };
            Console.WriteLine($"DEBUG_TURN: Карта добавлена в result.CardsToAdd: {drawnCard.Name}");

            // Определяем следующего игрока
            await DetermineNextPlayerAfterDraw(player);

            return result;
        }

        /// Определяет следующего игрока после взятия карты
        private async Task DetermineNextPlayerAfterDraw(Player currentPlayer)
        {
            Console.WriteLine($"DEBUG_TURN: DetermineNextPlayerAfterDraw для {currentPlayer.Nickname}");
            Console.WriteLine($"DEBUG_TURN: AttackTurnsRemaining={currentPlayer.AttackTurnsRemaining}");

            // Если игрок под атакой
            if (currentPlayer.AttackTurnsRemaining > 0)
            {
                currentPlayer.AttackTurnsRemaining--;
                Console.WriteLine($"DEBUG_TURN: Уменьшаем AttackTurns, осталось: {currentPlayer.AttackTurnsRemaining}");

                if (currentPlayer.AttackTurnsRemaining > 0)
                {
                    // Игрок продолжает ходить под атакой
                    Console.WriteLine($"DEBUG_TURN: Игрок {currentPlayer.Nickname} продолжает под атакой");
                    CurrentPlayer = currentPlayer;
                }
                else
                {
                    // Атака закончилась, передаем ход следующему
                    Console.WriteLine($"DEBUG_TURN: Атака закончилась, передаем ход следующему");
                    PassTurnToNextPlayer(currentPlayer);
                }
            }
            else
            {
                // Обычный ход завершен, передаем ход следующему
                Console.WriteLine($"DEBUG_TURN: Обычный ход завершен, передаем ход");
                PassTurnToNextPlayer(currentPlayer);
            }

            // ВАЖНО: Отправляем обновление игры после смены хода
            Console.WriteLine($"DEBUG_TURN: Отправляем BroadcastGameUpdate");
            await gameLogic.BroadcastGameUpdate();
        }

        /// <summary>
        /// Передает ход следующему игроку
        /// </summary>
        private void PassTurnToNextPlayer(Player currentPlayer)
        {
            Console.WriteLine($"DEBUG_TURN: PassTurnToNextPlayer от {currentPlayer.Nickname}");

            var nextPlayer = GetNextAlivePlayer(currentPlayer);

            if (nextPlayer != null && nextPlayer != currentPlayer)
            {
                Console.WriteLine($"DEBUG_TURN: Передача хода {currentPlayer.Nickname} -> {nextPlayer.Nickname}");
                CurrentPlayer = nextPlayer;
            }
            else if (nextPlayer == currentPlayer)
            {
                // Остался один игрок
                Console.WriteLine($"DEBUG_TURN: Остался один игрок: {currentPlayer.Nickname}");
                CurrentPlayer = currentPlayer;
            }
        }

        /// <summary>
        /// Получает следующего живого игрока
        /// </summary>
        private Player GetNextAlivePlayer(Player currentPlayer)
        {
            var alivePlayers = players.Where(p => p.IsAlive).ToList();
            if (alivePlayers.Count == 0) return null;

            int currentIndex = alivePlayers.IndexOf(currentPlayer);
            if (currentIndex == -1)
            {
                Console.WriteLine($"DEBUG_TURN: Текущий игрок не найден в живых, возвращаем первого");
                return alivePlayers[0];
            }

            int nextIndex = (currentIndex + 1) % alivePlayers.Count;
            var nextPlayer = alivePlayers[nextIndex];

            Console.WriteLine($"DEBUG_TURN: Следующий игрок: {currentIndex}->{nextIndex} = {nextPlayer.Nickname}");

            return nextPlayer;
        }

        private async Task<CardPlayResult> ProcessKittenDraw(Player player, Card kitten)
        {
            var result = new CardPlayResult
            {
                Success = true,
                DrawnCard = kitten,
                IsKitten = true,
                Message = $"{player.Nickname} вытянул Взрывного котёнка!"
            };

            Console.WriteLine($"DEBUG_TURN: Вытянут котёнок! HasDefuse: {player.HasDefuse}");

            // Проверяем наличие "Обезвредить"
            if (player.HasDefuse)
            {
                result.Defused = true;
                result.Message += " Но у него есть 'Обезвредить'!";
                Console.WriteLine($"DEBUG_TURN: Котёнок обезврежен");
            }
            else
            {
                // Игрок выбывает
                player.Eliminate();
                deck.Discard(kitten);
                result.Message += " Игрок выбывает из игры!";

                // Проверяем, не остался ли один игрок
                var alivePlayers = players.Where(p => p.IsAlive).ToList();
                if (alivePlayers.Count == 1)
                {
                    result.GameOver = true;
                    result.Winner = alivePlayers[0].Nickname;
                    result.Message += $" Игра окончена! Победитель: {result.Winner}";
                }

                Console.WriteLine($"DEBUG_TURN: Игрок выбыл, живых: {alivePlayers.Count}");
            }

            // Определяем следующего игрока
            await DetermineNextPlayerAfterDraw(player);

            return result;
        }

        /// <summary>
        /// Обработка обезвреживания котёнка
        /// </summary>
        public async Task<CardPlayResult> ProcessDefuse(Player player, Card kitten, int placement)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: ProcessDefuse для {player.Nickname}, размещение: {placement}");

            // Убираем "Обезвредить" из руки
            var defuseCard = player.GetCardByType(CardType.Defuse);
            if (defuseCard == null)
            {
                result.Success = false;
                result.Message = "У игрока нет карты 'Обезвредить'";
                return result;
            }

            player.RemoveCard(defuseCard);
            deck.Discard(defuseCard);

            // Размещаем котёнка обратно в колоду
            if (placement == 0) // Верх
            {
                deck.InsertCard(kitten, 0);
            }
            else if (placement == 1) // Низ
            {
                deck.InsertCard(kitten, deck.Cards.Count);
            }
            else if (placement == 2) // Случайно
            {
                int randomPos = new Random().Next(deck.Cards.Count + 1);
                deck.InsertCard(kitten, randomPos);
            }
            else // Конкретная позиция
            {
                deck.InsertCard(kitten, placement);
            }

            result.Success = true;
            result.Message = $"{player.Nickname} обезвредил котёнка и спрятал его в колоду!";

            // После обезвреживания ход заканчивается
            Console.WriteLine($"DEBUG_TURN: Обезвреживание завершено, передаём ход");
            await DetermineNextPlayerAfterDraw(player);

            return result;
        }

        // Обработка выбора цели для Favor/Steal/RequestCard
        public async Task<CardPlayResult> ProcessTargetSelection(Player initiator, string targetName, string requestedCard = "")
        {
            var result = new CardPlayResult();
            var target = players.FirstOrDefault(p => p.Nickname == targetName);

            Console.WriteLine($"DEBUG_TURN: ProcessTargetSelection: {initiator.Nickname} -> {targetName}, action: {PendingAction}");

            // 1. Проверка валидности цели
            if (target == null || !target.IsAlive || target == initiator)
            {
                result.Success = false;
                result.Message = "Неверная цель";
                return result;
            }

            // 2. Проверка возможности выбора цели сейчас
            if (!WaitingForTarget || ActionInitiator != initiator)
            {
                result.Success = false;
                result.Message = "Сейчас нельзя выбрать цель";
                return result;
            }

            // 3. Обработка в зависимости от типа действия
            if (PendingAction == "FAVOR")
            {
                return await ProcessFavorAction(initiator, target, result);
            }
            else if (PendingAction == "STEAL")
            {
                return await ProcessStealAction(initiator, target, result);
            }
            else if (PendingAction == "REQUEST_CARD")
            {
                return await ProcessRequestCardAction(initiator, target, requestedCard, result);
            }
            else
            {
                result.Success = false;
                result.Message = "Неизвестное действие";
                return result;
            }
        }

        private async Task<CardPlayResult> ProcessFavorAction(Player initiator, Player target, CardPlayResult result)
        {
            Console.WriteLine($"DEBUG_TURN: Processing Favor action");

            if (target.Hand.Count == 0)
            {
                result.Success = false;
                result.Message = "У цели нет карт";
                ResetTargetState();
                return result;
            }

            // Сохраняем состояние для Favor
            pendingFavorTarget = target;
            pendingFavorInitiator = initiator;
            waitingForCardChoice = true; // Ждем выбора карты

            Console.WriteLine($"DEBUG_TURN: Сохранено состояние Favor: {initiator.Nickname} -> {target.Nickname}");

            // Отправляем запрос выбора карты цели
            await SendCardChoiceRequest(target, initiator);

            result.Success = true;
            result.Message = $"Ожидание выбора карты от {target.Nickname}";
            result.WaitingForCardChoice = true;

            // НЕ сбрасываем состояние - ждем ответа от цели
            return result;
        }

        private async Task<CardPlayResult> ProcessStealAction(Player initiator, Player target, CardPlayResult result)
        {
            Console.WriteLine($"DEBUG_TURN: Processing Steal action");

            if (target.Hand.Count == 0)
            {
                result.Success = false;
                result.Message = "У цели нет карт";
                ResetTargetState();
                return result;
            }

            // Берем случайную карту
            var random = new Random();
            var stolenCard = target.Hand[random.Next(target.Hand.Count)];

            target.RemoveCard(stolenCard);
            initiator.AddCard(stolenCard);

            result.Success = true;
            result.Message = $"{initiator.Nickname} украл карту у {target.Nickname}";
            result.CardsToAdd = new List<Card> { stolenCard };

            // После кражи игрок должен завершить ход
            initiator.MustDrawCard = true;

            ResetTargetState();
            await gameLogic.BroadcastGameUpdate();

            return result;
        }

        private async Task<CardPlayResult> ProcessRequestCardAction(Player initiator, Player target, string requestedCard, CardPlayResult result)
        {
            Console.WriteLine($"DEBUG_TURN: Processing Request Card action: '{requestedCard}'");

            if (string.IsNullOrWhiteSpace(requestedCard))
            {
                result.Success = false;
                result.Message = "Не указана карта для запроса";
                ResetTargetState();
                return result;
            }

            // Ищем карту с указанным названием
            var requested = target.Hand.FirstOrDefault(c =>
                c.Name.Contains(requestedCard, StringComparison.OrdinalIgnoreCase));

            if (requested != null)
            {
                target.RemoveCard(requested);
                initiator.AddCard(requested);

                result.Success = true;
                result.Message = $"{initiator.Nickname} получил карту '{requestedCard}' от {target.Nickname}";
                result.CardsToAdd = new List<Card> { requested };

                // После получения карты игрок должен завершить ход
                initiator.MustDrawCard = true;

                ResetTargetState();
                await gameLogic.BroadcastGameUpdate();
            }
            else
            {
                result.Success = false;
                result.Message = $"У {target.Nickname} нет карты '{requestedCard}'";
                ResetTargetState();
            }

            return result;
        }

        private void ResetTargetState()
        {
            WaitingForTarget = false;
            PendingAction = "";
            ActionInitiator = null;

            // Не сбрасываем состояние Favor если ждем выбора карты
            if (!waitingForCardChoice)
            {
                pendingFavorTarget = null;
                pendingFavorInitiator = null;
            }

            Console.WriteLine($"DEBUG_TURN: Состояние выбора цели сброшено");
        }

        // Сбрасывает состояние хода
        public void ResetTurnState()
        {
            nopeCount = 0;
            isNoped = false;
            nopeStack.Clear();
            lastPlayedCard = null;
            currentCombo.Clear();
            WaitingForTarget = false;
            PendingAction = "";
            ActionInitiator = null;
        }

        // Обработка выбора карты для Favor
        public async Task<CardPlayResult> ProcessCardChoice(Player target, int cardId)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: ProcessCardChoice: {target.Nickname} выбрал карту ID: {cardId}");

            // Проверяем, есть ли ожидание выбора карты для Favor
            if (pendingFavorTarget == null || pendingFavorInitiator == null || pendingFavorTarget != target)
            {
                result.Success = false;
                result.Message = "Нельзя выбрать карту сейчас";
                return result;
            }

            // Находим карту по ID
            var cardToGive = target.Hand.FirstOrDefault(c => c.Id == cardId);
            if (cardToGive == null)
            {
                result.Success = false;
                result.Message = "Карта не найдена";
                return result;
            }

            // Передаем карту
            target.RemoveCard(cardToGive);
            pendingFavorInitiator.AddCard(cardToGive);

            result.Success = true;
            result.Message = $"{target.Nickname} дал карту {pendingFavorInitiator.Nickname}";
            result.CardsToAdd = new List<Card> { cardToGive };

            // После Favor инициатор должен завершить ход взятием карты
            pendingFavorInitiator.MustDrawCard = true;

            // Сбрасываем состояние
            pendingFavorTarget = null;
            pendingFavorInitiator = null;
            WaitingForTarget = false;
            PendingAction = "";
            ActionInitiator = null;

            Console.WriteLine($"DEBUG_TURN: Favor завершен. {pendingFavorInitiator?.Nickname} должен тянуть карту");

            // Отправляем обновление
            await gameLogic.BroadcastGameUpdate();

            return result;
        }

        private async Task SendCardChoiceRequest(Player target, Player initiator)
        {
            try
            {
                var request = new
                {
                    Message = $"Игрок {initiator.Nickname} просит у вас карту. Выберите карту для передачи:",
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

                Console.WriteLine($"DEBUG_TURN: Отправляем запрос выбора карты игроку {target.Nickname}");
                target.SendPacket(ExplodingKittensProtocol.REQUEST_CARD_SELECTION, bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_TURN: Ошибка отправки запроса выбора карты: {ex.Message}");
            }
        }

        public async Task<CardPlayResult> ProcessCardSelection(Player player, int cardId)
        {
            var result = new CardPlayResult();

            Console.WriteLine($"DEBUG_TURN: ProcessCardSelection: игрок {player.Nickname}, карта ID: {cardId}");

            // Проверяем, что это игрок ожидает выбора карты для Favor
            if (!waitingForCardChoice || pendingFavorTarget != player || pendingFavorInitiator == null)
            {
                result.Success = false;
                result.Message = "Сейчас нельзя выбрать карту";
                return result;
            }

            // Находим карту
            var card = player.Hand.FirstOrDefault(c => c.Id == cardId);
            if (card == null)
            {
                result.Success = false;
                result.Message = "Карта не найдена в руке";
                return result;
            }

            // Передаем карту
            player.RemoveCard(card);
            pendingFavorInitiator.AddCard(card);

            result.Success = true;
            result.Message = $"{player.Nickname} передал карту {pendingFavorInitiator.Nickname}";
            result.CardsToAdd = new List<Card> { card };

            // После передачи карты инициатор должен завершить ход
            pendingFavorInitiator.MustDrawCard = true;

            // Сбрасываем все состояния
            waitingForCardChoice = false;
            WaitingForTarget = false;
            PendingAction = "";
            ActionInitiator = null;
            var tempInitiator = pendingFavorInitiator;
            pendingFavorTarget = null;
            pendingFavorInitiator = null;

            Console.WriteLine($"DEBUG_TURN: Карта передана. Теперь {tempInitiator?.Nickname} должен завершить ход");

            // Отправляем обновление
            await gameLogic.BroadcastGameUpdate();

            return result;
        }
    }
}

