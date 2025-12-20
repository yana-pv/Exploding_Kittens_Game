using Shared.Models;
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
    public int ExtraTurns { get; set; }


    [JsonIgnore]
    public bool HasDefuseCard => Hand.Any(c => c.Type == CardType.Defuse);

    public void AddToHand(Card card)
    {
        Hand.Add(card);
    }

    public Card RemoveCard(CardType type)
    {
        var card = Hand.FirstOrDefault(c => c.Type == type);
        if (card != null)
        {
            Hand.Remove(card);
        }

        return card!;
    }

    public bool HasCard(CardType type) => Hand.Any(c => c.Type == type);
}