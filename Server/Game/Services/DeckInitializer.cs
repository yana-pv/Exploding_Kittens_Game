using Shared.Models;

namespace Server.Game.Services;

public class DeckInitializer
{
    public static (List<Card> deck, List<List<Card>> playerHands) CreateGameSetup(int playerCount)
    {
        if (playerCount < 2 || playerCount > 5)
        {
            throw new ArgumentException("Поддерживается 2-5 игроков");
        }

        var random = new Random();

        // Создаем колоду без Взрывных Котят и Обезвредить
        var deckWithoutExplosives = new List<Card>();

        AddCards(deckWithoutExplosives, CardType.Nope, 5);
        AddCards(deckWithoutExplosives, CardType.Attack, 4);
        AddCards(deckWithoutExplosives, CardType.Skip, 4);
        AddCards(deckWithoutExplosives, CardType.Favor, 4);
        AddCards(deckWithoutExplosives, CardType.Shuffle, 4);
        AddCards(deckWithoutExplosives, CardType.SeeTheFuture, 5);

        AddCards(deckWithoutExplosives, CardType.RainbowCat, 4);
        AddCards(deckWithoutExplosives, CardType.BeardCat, 4);
        AddCards(deckWithoutExplosives, CardType.PotatoCat, 4);
        AddCards(deckWithoutExplosives, CardType.WatermelonCat, 4);
        AddCards(deckWithoutExplosives, CardType.TacoCat, 4);

        // Перемешиваем
        Shuffle(deckWithoutExplosives, random);

        // Раздаем по 4 карты каждому игроку
        var playerHands = new List<List<Card>>();
        for (int i = 0; i < playerCount; i++)
        {
            var hand = new List<Card>();
            for (int j = 0; j < 4; j++)
            {
                if (deckWithoutExplosives.Count == 0) break;
                hand.Add(deckWithoutExplosives[0]);
                deckWithoutExplosives.RemoveAt(0);
            }
            playerHands.Add(hand);
        }

        // Добавляем по 1 Обезвредить каждой руке
        foreach (var hand in playerHands)
        {
            hand.Add(Card.Create(CardType.Defuse));
        }

        // Создаем финальную колоду из оставшихся карт + Взрывные Котята + оставшиеся Обезвредить
        var finalDeck = new List<Card>(deckWithoutExplosives);

        // Добавляем Взрывных Котят (игроки - 1)
        AddCards(finalDeck, CardType.ExplodingKitten, playerCount - 1);

        // Добавляем оставшиеся Обезвредить
        int remainingDefuses;
        if (playerCount == 2)
        {
            // Для 2 игроков: 2 дополнительные карты Обезвредить
            remainingDefuses = 2;
        }
        else
        {
            // Для 3+ игроков: 6 - playerCount 
            remainingDefuses = 6 - playerCount;
            if (remainingDefuses < 0) remainingDefuses = 0;
        }

        AddCards(finalDeck, CardType.Defuse, remainingDefuses);

        // Перемешиваем финальную колоду
        Shuffle(finalDeck, random);

        return (finalDeck, playerHands);
    }

    private static void AddCards(List<Card> cards, CardType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            cards.Add(Card.Create(type));
        }
    }

    private static void Shuffle(List<Card> cards, Random random)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }
}