using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace Server.Game.Models;

public class Player
{
    public required Socket Connection { get; set; }
    public required Guid Id { get; set; }
    public string Name { get; set; } = "Игрок";
    public List<Card> Hand { get; } = new();
    public bool IsAlive { get; set; } = true;
    public int TurnOrder { get; set; }
    public bool HasPendingAction { get; set; }
    public int ExtraTurns { get; set; }
    public bool IsProtected { get; set; }

    [JsonIgnore]
    public bool HasDefuseCard => Hand.Any(c => c.Type == CardType.Defuse);

    [JsonIgnore]
    public int CardCount => Hand.Count;

    public void AddToHand(Card card)
    {
        Hand.Add(card);
        Hand.Sort((a, b) => a.Type.CompareTo(b.Type));

        // Если есть CardCounter, обновляем его
        // (Этот код будет работать, если CardCounter передается в Player)
        // Или обновление делается в GameSession при раздаче
    }

    public Card RemoveCard(CardType type)
    {
        var card = Hand.FirstOrDefault(c => c.Type == type);
        if (card != null)
            Hand.Remove(card);
        return card!;
    }

    public Card RemoveCardAt(int index)
    {
        if (index < 0 || index >= Hand.Count)
            return null!;

        var card = Hand[index];
        Hand.RemoveAt(index);
        return card;
    }

    public bool HasCard(CardType type) => Hand.Any(c => c.Type == type);

    public bool HasCardsForCombo(int count, bool sameType = false)
    {
        if (sameType)
            return Hand.GroupBy(c => c.Type).Any(g => g.Count() >= count);

        return Hand.Select(c => c.IconId).Distinct().Count() >= 5;
    }

    public List<List<Card>> GetAvailableCombos()
    {
        var combos = new List<List<Card>>();

        // Проверяем пары (2 одинаковые)
        var pairs = Hand
            .GroupBy(c => c.Type)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Take(2).ToList());
        combos.AddRange(pairs);

        // Проверяем тройки (3 одинаковые)
        var triples = Hand
            .GroupBy(c => c.Type)
            .Where(g => g.Count() >= 3)
            .Select(g => g.Take(3).ToList());
        combos.AddRange(triples);

        // Проверяем 5 разных по иконкам
        var distinctIcons = Hand
            .GroupBy(c => c.IconId)
            .Select(g => g.First())
            .ToList();

        if (distinctIcons.Count >= 5)
        {
            // Находим все комбинации из 5 разных иконок
            combos.AddRange(GetCombinations(distinctIcons, 5));
        }

        return combos;
    }

    private static IEnumerable<List<Card>> GetCombinations(List<Card> cards, int k)
    {
        if (k > cards.Count) yield break;

        var indices = Enumerable.Range(0, k).ToArray();
        yield return indices.Select(i => cards[i]).ToList();

        while (true)
        {
            var i = k - 1;
            while (i >= 0 && indices[i] == cards.Count - k + i)
                i--;

            if (i < 0) yield break;

            indices[i]++;
            for (var j = i + 1; j < k; j++)
                indices[j] = indices[j - 1] + 1;

            yield return indices.Select(idx => cards[idx]).ToList();
        }
    }
}