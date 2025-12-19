using Shared.Models;

namespace Server.Game.Services;

public class CardCounter
{
    private readonly Dictionary<CardType, int> _cardsInDeck = new();
    private readonly Dictionary<CardType, int> _cardsInDiscard = new();
    private readonly Dictionary<CardType, int> _cardsInHands = new();

    public void Initialize(List<Card> initialDeck)
    {
        foreach (var card in initialDeck)
        {
            IncrementCount(card.Type, CardLocation.Deck);
        }
    }

    public void CardMoved(CardType type, CardLocation from, CardLocation to)
    {
        DecrementCount(type, from);
        IncrementCount(type, to);
    }

    public int GetCount(CardType type, CardLocation location)
    {
        return location switch
        {
            CardLocation.Deck => _cardsInDeck.GetValueOrDefault(type, 0),
            CardLocation.Discard => _cardsInDiscard.GetValueOrDefault(type, 0),
            CardLocation.Hand => _cardsInHands.GetValueOrDefault(type, 0),
            _ => 0
        };
    }

    public int GetTotalRemaining(CardType type)
    {
        return GetCount(type, CardLocation.Deck) +
               GetCount(type, CardLocation.Discard) +
               GetCount(type, CardLocation.Hand);
    }

    private void IncrementCount(CardType type, CardLocation location)
    {
        var dict = GetDictionary(location);
        dict[type] = dict.GetValueOrDefault(type, 0) + 1;
    }

    private void DecrementCount(CardType type, CardLocation location)
    {
        var dict = GetDictionary(location);
        if (dict.ContainsKey(type) && dict[type] > 0)
        {
            dict[type]--;
        }
    }

    private Dictionary<CardType, int> GetDictionary(CardLocation location)
    {
        return location switch
        {
            CardLocation.Deck => _cardsInDeck,
            CardLocation.Discard => _cardsInDiscard,
            CardLocation.Hand => _cardsInHands,
            _ => throw new ArgumentException($"Unknown location: {location}")
        };
    }
}

public enum CardLocation
{
    Deck,
    Discard,
    Hand
}
