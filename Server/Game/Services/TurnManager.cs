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
    private bool _skipPlayed = false;   // Был ли сыгран Skip
    private bool _attackPlayed = false; // Был ли сыгран Attack

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

        return _session.State == GameState.PlayerTurn;
    }

    public bool CanPlayAnotherCard()
    {
        // Можно играть сколько угодно карт, пока не взял карту
        // и не сыграли Skip/Attack (они завершают ход)
        return CanPlayCard() && !_skipPlayed && !_attackPlayed;
    }

    public bool MustDrawCard()
    {
        // Игрок должен взять карту перед завершением хода
        // Если сыграли Skip или Attack - НЕ нужно брать карту
        return !_hasDrawnCard && !_skipPlayed && !_attackPlayed;
    }

    public void CardPlayed(Card card)
    {
        _cardsPlayedThisTurn++;
        _playedCards.Add(card);

        // Если сыграли Skip или Attack, помечаем что ход заканчивается без взятия карты
        if (card.Type == CardType.Skip)
        {
            _skipPlayed = true;
            _turnEnded = true; // Skip завершает ход
        }
        else if (card.Type == CardType.Attack)
        {
            _attackPlayed = true;
            _turnEnded = true; // Attack завершает ход
        }
    }

    public void CardDrawn()
    {
        _hasDrawnCard = true;
        _turnEnded = true; // После взятия карты ход завершается
    }

    public void EndTurn()
    {
        if (_turnEnded)
            return;

        // Проверяем, должен ли игрок взять карту
        if (MustDrawCard())
        {
            throw new InvalidOperationException("Нельзя завершить ход без взятия карты! Используйте команду draw");
        }

        _turnEnded = true;
    }

    public void ForceEndTurn()
    {
        // Используется для принудительного завершения хода (например, при выбывании игрока)
        _turnEnded = true;
    }

    private void Reset()
    {
        _cardsPlayedThisTurn = 0;
        _hasDrawnCard = false;
        _turnEnded = false;
        _skipPlayed = false;
        _attackPlayed = false;
        _playedCards.Clear();
    }

    // Метод для завершения хода и перехода к следующему игроку
    public async Task CompleteTurnAsync()
    {
        if (!_turnEnded)
        {
            // Если ход еще не завершен, но должен быть
            if (MustDrawCard())
            {
                throw new InvalidOperationException("Игрок должен взять карту перед завершением хода!");
            }

            _turnEnded = true;
        }

        // Переходим к следующему игроку
        _session.NextPlayer();

        // Сбрасываем состояние для нового хода
        Reset();
    }

    public int CardsPlayedCount => _cardsPlayedThisTurn;
    public bool HasDrawnCard => _hasDrawnCard;
    public bool TurnEnded => _turnEnded;
    public bool SkipPlayed => _skipPlayed;
    public bool AttackPlayed => _attackPlayed;
    public bool MustDrawCardBeforeEnd => MustDrawCard();
}