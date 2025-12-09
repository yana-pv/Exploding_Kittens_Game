// Common/Deck.cs
namespace Common
{
    public class Deck
    {
        public List<Card> Cards { get; private set; }
        public List<Card> DiscardPile { get; private set; }
        private Random random;

        // Статистика для балансировки
        public int ExplodingKittensCount => 4;
        public int DefuseCardsCount => 6;
        public Dictionary<CardType, int> ActionCards = new()
        {
            { CardType.Attack, 4 },
            { CardType.Skip, 4 },
            { CardType.Favor, 4 },
            { CardType.Shuffle, 4 },
            { CardType.SeeTheFuture, 5 },
            { CardType.Nope, 5 }
        };

        // Маппинг названий котиков на типы карт
        private Dictionary<string, CardType> CatNameToType = new()
        {
            { "Taco", CardType.TacoCat },
            { "Potato", CardType.PotatoCat },
            { "Beard", CardType.BeardCat },
            { "Rainbow", CardType.RainbowCat },
            { "Caticorn", CardType.CaticornCat }
        };

        public Deck()
        {
            Cards = new List<Card>();
            DiscardPile = new List<Card>();
            random = new Random();
            InitializeDeck();
        }

        private void InitializeDeck()
        {
            int id = 1;

            // 1. Взрывные котята (4 штуки) - НЕ создаем здесь!
            // Они будут созданы отдельно в PrepareForGame
            Console.WriteLine("DEBUG_DECK: Взрывные котята не создаются в InitializeDeck");

            // 2. Обезвредить (6 штук)
            for (int i = 0; i < DefuseCardsCount; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.Defuse,
                    Category = CardCategory.Action,
                    Name = "Обезвредить",
                    Description = "Спасает от взрывного котёнка. Спрячьте котёнка обратно в колоду.",
                    Icon = "Defuse"
                });
            }

            // 3. Карты действий
            // Атаковать (4)
            for (int i = 0; i < ActionCards[CardType.Attack]; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.Attack,
                    Category = CardCategory.Action,
                    Name = "Атаковать",
                    Description = "Заканчивает ваш ход. Следующий игрок ходит 2 раза подряд.",
                    Icon = "Attack"
                });
            }

            // Пропустить (4)
            for (int i = 0; i < ActionCards[CardType.Skip]; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.Skip,
                    Category = CardCategory.Action,
                    Name = "Пропустить",
                    Description = "Немедленно заканчивает ваш ход без взятия карты.",
                    Icon = "Skip"
                });
            }

            // Одолжить (4)
            for (int i = 0; i < ActionCards[CardType.Favor]; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.Favor,
                    Category = CardCategory.Action,
                    Name = "Одолжить",
                    Description = "Выберите игрока. Он даёт вам одну карту из своей руки.",
                    Icon = "Favor"
                });
            }

            // Перемешать (4)
            for (int i = 0; i < ActionCards[CardType.Shuffle]; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.Shuffle,
                    Category = CardCategory.Action,
                    Name = "Перемешать",
                    Description = "Тщательно перемешайте колоду.",
                    Icon = "Shuffle"
                });
            }

            // Заглянуть в будущее (5)
            for (int i = 0; i < ActionCards[CardType.SeeTheFuture]; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.SeeTheFuture,
                    Category = CardCategory.Action,
                    Name = "Заглянуть в будущее",
                    Description = "Посмотрите 3 верхние карты колоды. Не показывайте другим.",
                    Icon = "Future"
                });
            }

            // Нет (5)
            for (int i = 0; i < ActionCards[CardType.Nope]; i++)
            {
                Cards.Add(new Card
                {
                    Id = id++,
                    Type = CardType.Nope,
                    Category = CardCategory.Action,
                    Name = "Нет",
                    Description = "Остановите любое действие (кроме Обезвредить и Взрывного котёнка).",
                    Icon = "Nope"
                });
            }

            // 4. Котики для пар (20 карт, 5 типов по 4)
            int catIndex = 0;
            foreach (var catName in CatNameToType.Keys)
            {
                var catType = CatNameToType[catName];
                for (int j = 0; j < 4; j++) // По 4 каждого типа
                {
                    Cards.Add(new Card
                    {
                        Id = id++,
                        Type = catType,
                        Category = CardCategory.Cat,
                        Icon = catName,
                        Name = $"Котик {catName}",
                        Description = "Используется для создания пар и комбо."
                    });
                }
                catIndex++;
            }
        }

        /// <summary>
        /// Подготовка колоды по правилам игры
        /// Возвращает карты "Обезвредить" для раздачи игрокам
        /// </summary>
        public List<Card> PrepareForGame(int playerCount)
        {
            Console.WriteLine($"Подготовка колоды для {playerCount} игроков");

            // 1. Убрать "Обезвредить" из колоды для раздачи игрокам
            var allDefuses = Cards.Where(c => c.Type == CardType.Defuse).ToList();
            Cards.RemoveAll(c => c.Type == CardType.Defuse);
            Console.WriteLine($"Убрано {allDefuses.Count} карт 'Обезвредить' из колоды");

            // 2. Перемешать остальные карты (взрывных котят в них нет)
            Shuffle();
            Console.WriteLine($"После удаления обезвредить: {Cards.Count} карт");

            // 3. Взрывные котята создаются здесь
            int nextId = Cards.Count > 0 ? Cards.Max(c => c.Id) + 1 : 1; // Получаем следующий доступный ID
            int kittensToReturn = playerCount - 1;

            Console.WriteLine($"Создаем {kittensToReturn} взрывных котят");
            for (int i = 0; i < kittensToReturn; i++)
            {
                Cards.Add(new Card
                {
                    Id = nextId++,
                    Type = CardType.ExplodingKitten,
                    Category = CardCategory.Kitten,
                    Name = "Взрывной котёнок",
                    Description = "Если у вас нет 'Обезвредить' - вы проиграли!",
                    Icon = "Exploding"
                });
            }

            // 4. Вернуть "Обезвредить" в колоду (кроме тех, что раздадим игрокам)
            int defusesToReturn;

            if (playerCount == 2)
            {
                defusesToReturn = 2; // Специальное правило для 2 игроков
                Console.WriteLine("Специальное правило для 2 игроков: только 2 'Обезвредить' в колоде");
            }
            else
            {
                defusesToReturn = DefuseCardsCount - playerCount;
                Console.WriteLine($"Возвращаем {defusesToReturn} карт 'Обезвредить' в колоду");
            }

            // Берем первые defusesToReturn карт для колоды
            List<Card> defusesForDeck = new List<Card>();
            for (int i = 0; i < defusesToReturn && i < allDefuses.Count; i++)
            {
                defusesForDeck.Add(allDefuses[i]);
                Cards.Add(allDefuses[i]);
            }

            // 5. Остальные карты "Обезвредить" откладываем для раздачи игрокам
            List<Card> defusesForPlayers = new List<Card>();
            for (int i = defusesToReturn; i < allDefuses.Count && defusesForPlayers.Count < playerCount; i++)
            {
                defusesForPlayers.Add(allDefuses[i]);
            }

            // 6. Если не хватает карт для игроков, создаем новые
            while (defusesForPlayers.Count < playerCount)
            {
                defusesForPlayers.Add(new Card
                {
                    Id = nextId++,
                    Type = CardType.Defuse,
                    Category = CardCategory.Action,
                    Name = "Обезвредить",
                    Description = "Спасает от взрывного котёнка. Спрячьте котёнка обратно в колоду.",
                    Icon = "Defuse"
                });
                Console.WriteLine($"Создана дополнительная карта 'Обезвредить' для игрока");
            }

            // 7. Финальное перемешивание
            Shuffle();

            Console.WriteLine($"Колода готова: {Cards.Count} карт, {kittensToReturn} котят, {defusesToReturn} обезвредить");
            Console.WriteLine($"Карт 'Обезвредить' для игроков: {defusesForPlayers.Count}");

            return defusesForPlayers;
        }

        public void Shuffle()
        {
            int n = Cards.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int k = random.Next(i + 1);
                (Cards[i], Cards[k]) = (Cards[k], Cards[i]);
            }
        }

        public Card Draw()
        {
            if (Cards.Count == 0)
            {
                // РЕДКИЙ СЛУЧАЙ: если колода пуста, перемешиваем сброс
                Console.WriteLine("Колода пуста, перемешиваем сброс...");
                Cards = new List<Card>(DiscardPile);
                DiscardPile.Clear();
                Shuffle();
            }

            var card = Cards[0];
            Cards.RemoveAt(0);
            return card;
        }

        public Card DrawFromBottom()
        {
            if (Cards.Count == 0)
            {
                Cards = new List<Card>(DiscardPile);
                DiscardPile.Clear();
                Shuffle();
            }

            var card = Cards[^1];
            Cards.RemoveAt(Cards.Count - 1);
            return card;
        }

        public Card DrawAtPosition(int position)
        {
            if (position < 0 || position >= Cards.Count)
                return Draw(); // По умолчанию сверху

            var card = Cards[position];
            Cards.RemoveAt(position);
            return card;
        }

        public void InsertCard(Card card, int position)
        {
            if (position < 0) position = 0;
            if (position > Cards.Count) position = Cards.Count;

            Cards.Insert(position, card);
        }

        public void Discard(Card card)
        {
            DiscardPile.Add(card);
        }

        public Card TakeFromDiscard()
        {
            if (DiscardPile.Count == 0)
                throw new InvalidOperationException("Сброс пуст");

            var card = DiscardPile[^1];
            DiscardPile.RemoveAt(DiscardPile.Count - 1);
            return card;
        }

        public List<Card> PeekTopCards(int count)
        {
            count = Math.Min(count, Cards.Count);
            return Cards.Take(count).ToList();
        }

        public int CountByType(CardType type) =>
            Cards.Count(c => c.Type == type) + DiscardPile.Count(c => c.Type == type);
    }
}