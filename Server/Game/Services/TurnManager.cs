using Server.Game.Enums;
using Server.Game.Models;

namespace Server.Game.Services;

public class TurnManager
{
    private readonly GameSession _session;
    private int _cardsPlayedThisTurn = 0;
    private bool _hasDrawnCard = false;
    private bool _turnEnded = false;
    private readonly List<Card> _playedCards = new();
    private bool _attackPlayed = false; // Был ли сыгран Attack в этом ходу

    public TurnManager(GameSession session)
    {
        _session = session;
    }

    public bool CanPlayCard()
    {
        if (_turnEnded)
            return false;

        if (_hasDrawnCard)
            return false; // Уже взял карту - ход завершен

        if (_attackPlayed)
            return false; // Сыграли Attack - ход завершен

        return _session.State == GameState.PlayerTurn;
    }

    public bool CanPlayAnotherCard()
    {
        // Можно играть сколько угодно карт за ход, пока не взял карту и не сыграл Attack
        return CanPlayCard();
    }

    public bool MustDrawCard()
    {
        // Игрок должен взять карту перед завершением хода
        // Если сыграли Attack - можно не брать
        return !_hasDrawnCard && !_attackPlayed;
    }

    public void CardPlayed(Card card)
    {
        _cardsPlayedThisTurn++;
        _playedCards.Add(card);

        // Если сыграли Attack, помечаем что ход заканчивается без взятия карты
        if (card.Type == CardType.Attack)
        {
            _attackPlayed = true;
        }
    }

    public void CardDrawn()
    {
        _hasDrawnCard = true;
        _turnEnded = true; // После взятия карты ход завершается

        // Автоматический переход к следующему игроку
        Reset();
    }

    public void EndTurn()
    {
        if (_turnEnded)
            return;

        // Если игрок не взял карту и не сыграл Attack - нельзя завершить ход
        if (!_hasDrawnCard && !_attackPlayed)
        {
            throw new InvalidOperationException("Нельзя завершить ход без взятия карты!");
        }

        _turnEnded = true;
        Reset();
    }

    public void ForceEndTurn()
    {
        // Используется для принудительного завершения хода (например, при выбывании игрока)
        _turnEnded = true;
        Reset();
    }

    private void Reset()
    {
        _cardsPlayedThisTurn = 0;
        _hasDrawnCard = false;
        _turnEnded = false;
        _attackPlayed = false;
        _playedCards.Clear();
    }

    public int CardsPlayedCount => _cardsPlayedThisTurn;
    public bool HasDrawnCard => _hasDrawnCard;
    public bool TurnEnded => _turnEnded;
    public bool AttackPlayed => _attackPlayed;
}
