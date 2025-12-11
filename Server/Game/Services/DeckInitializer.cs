using Server.Game.Models;

namespace Server.Game.Services;

public class DeckInitializer
{
    public static List<Card> CreateDeckForPlayers(int playerCount)
    {
        var cards = new List<Card>();

        // Проверка на максимальное количество игроков
        if (playerCount > 5)
        {
            throw new InvalidOperationException(
                "Для более 5 игроков нужно объединять несколько колод. " +
                "В этой реализации поддерживается до 5 игроков.");
        }

        // 1. ВЗРЫВНЫЕ КОТЯТА: всегда 4 карты в игре
        // Но в колоду кладем на 1 меньше, чем игроков
        int explodingKittensInDeck = playerCount - 1;
        AddCards(cards, CardType.ExplodingKitten, explodingKittensInDeck);

        // 2. ОБЕЗВРЕДИТЬ: всегда 6 карт в игре
        // Раздаем по 1 каждому игроку, остальные в колоду
        int defuseInDeck = 6 - playerCount; // Остается в колоде
        if (defuseInDeck < 0) defuseInDeck = 0;

        AddCards(cards, CardType.Defuse, defuseInDeck);

        // 3. Остальные карты (полная колода)
        AddCards(cards, CardType.Nope, 5);
        AddCards(cards, CardType.Attack, 4);
        AddCards(cards, CardType.Skip, 4);
        AddCards(cards, CardType.Favor, 4);
        AddCards(cards, CardType.Shuffle, 4);
        AddCards(cards, CardType.SeeTheFuture, 5);

        // Карты котов (по 4 каждого вида)
        AddCards(cards, CardType.RainbowCat, 4);
        AddCards(cards, CardType.BeardCat, 4);
        AddCards(cards, CardType.PotatoCat, 4);
        AddCards(cards, CardType.WatermelonCat, 4);
        AddCards(cards, CardType.TacoCat, 4);

        // Проверяем общее количество карт
        var totalCardsNeeded = (playerCount * 5) + explodingKittensInDeck;
        if (cards.Count < totalCardsNeeded)
        {
            // Добавляем дополнительные карты котов, если нужно
            int cardsToAdd = totalCardsNeeded - cards.Count;
            for (int i = 0; i < cardsToAdd; i++)
            {
                cards.Add(Card.Create(CardType.RainbowCat));
            }
        }

        return cards;
    }

    private static void AddCards(List<Card> cards, CardType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            cards.Add(Card.Create(type));
        }
    }
}
