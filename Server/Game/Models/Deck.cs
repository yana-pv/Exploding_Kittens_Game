using System.Text.Json.Serialization;

namespace Server.Game.Models;

public class Deck
{
    private Stack<Card> _drawPile = new();
    private readonly List<Card> _discardPile = new();
    private readonly Random _random = new();

    [JsonIgnore]
    public int CardsRemaining => _drawPile.Count;

    [JsonIgnore]
    public IReadOnlyList<Card> DiscardPile => _discardPile.AsReadOnly();

    [JsonIgnore]
    public bool IsEmpty => _drawPile.Count == 0;

    public void Initialize(List<Card> cards)
    {
        Shuffle(cards);
        _drawPile = new Stack<Card>(cards);
    }

    private void Shuffle(List<Card> cards)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    private void AddCards(List<Card> cards, CardType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            cards.Add(Card.Create(type));
        }
    }

    public Card Draw()
    {
        if (_drawPile.Count == 0)
        {
            // Перемешиваем сброс в колоду
            ReshuffleDiscard();

            if (_drawPile.Count == 0)
                throw new InvalidOperationException("Колода полностью пуста");
        }

        return _drawPile.Pop();
    }

    public List<Card> PeekTop(int count)
    {
        // Берем карты не удаляя их из колоды
        return _drawPile.Take(count).ToList();
    }

    public bool CanPeek(int count)
    {
        return _drawPile.Count >= count;
    }

    public void Discard(Card card)
    {
        _discardPile.Add(card);
    }

    public Card TakeFromDiscard(int index)
    {
        if (index < 0 || index >= _discardPile.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var card = _discardPile[index];
        _discardPile.RemoveAt(index);
        return card;
    }

    public void ShuffleDeck()
    {
        var cards = _drawPile.ToList();
        Shuffle(cards);
        _drawPile = new Stack<Card>(cards);
    }

    public void InsertCard(Card card, int positionFromTop = 0)
    {
        var cards = _drawPile.ToList();
        positionFromTop = Math.Min(positionFromTop, cards.Count);
        cards.Insert(positionFromTop, card);
        _drawPile = new Stack<Card>(cards);
    }

    private void ReshuffleDiscard()
    {
        if (_discardPile.Count == 0)
            return;

        var cards = _discardPile.ToList();
        Shuffle(cards);
        _drawPile = new Stack<Card>(cards);
        _discardPile.Clear();

        Console.WriteLine($"Колода перемешана из сброса. Карт: {_drawPile.Count}");
    }
}