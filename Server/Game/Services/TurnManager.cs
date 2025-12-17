using Server.Game.Enums;
using Server.Game.Models;
using Server.Networking;

namespace Server.Game.Services;

public class TurnManager
{
    private readonly GameSession _session;
    private int _cardsPlayedThisTurn = 0;
    private bool _hasDrawnCard = false;
    private bool _turnEnded = false;
    private readonly List<Card> _playedCards = new();
    private bool _skipPlayed = false;   
    private bool _attackPlayed = false; 
    private int _extraTurnsRemaining = 0; 

    public TurnManager(GameSession session)
    {
        _session = session;
    }

    public bool CanPlayCard()
    {
        if (_turnEnded)
            return false;

        if (_hasDrawnCard)
            return false; 

        return _session.State == GameState.PlayerTurn;
    }

    public bool CanPlayAnotherCard()
    {
        return CanPlayCard() && !_skipPlayed && !_attackPlayed;
    }

    public bool MustDrawCard()
    {
        return !_hasDrawnCard && !_skipPlayed && !_attackPlayed;
    }

    public void CardPlayed(Card card)
    {
        _cardsPlayedThisTurn++;
        _playedCards.Add(card);

        if (card.Type == CardType.Skip)
        {
            _skipPlayed = true;
            _turnEnded = true; 
        }
        else if (card.Type == CardType.Attack)
        {
            _attackPlayed = true;
            _turnEnded = true; 

            // Проверяем, является ли игрок жертвой предыдущей атаки
            if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
            {
                _session.CurrentPlayer.ExtraTurns = 0;

                MarkNextPlayerAsAttacked();
            }
            else
            {
                MarkNextPlayerAsAttacked();
            }
        }
    }

    private void MarkNextPlayerAsAttacked(Player fromPlayer = null)
    {
        var startPlayer = fromPlayer ?? _session.CurrentPlayer;
        if (startPlayer == null)
            return;

        var currentIndex = _session.Players.IndexOf(startPlayer);
        var players = _session.Players;
        var attempts = 0;

        do
        {
            currentIndex = (currentIndex + 1) % players.Count;
            attempts++;

            if (attempts > players.Count)
                return;
        }
        while (!players[currentIndex].IsAlive);

        var attackedPlayer = players[currentIndex];
        attackedPlayer.ExtraTurns = 1;
    }

    public void CardDrawn()
    {
        _hasDrawnCard = true;

        // Проверяем, нужно ли игроку ходить еще раз (если он атакован)
        if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
        {
            _session.CurrentPlayer.ExtraTurns--;

            ResetForNextTurn();
        }
        else
        {
            _turnEnded = true;
        }
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


    private void Reset()
    {
        _cardsPlayedThisTurn = 0;
        _hasDrawnCard = false;
        _turnEnded = false;
        _skipPlayed = false;
        _attackPlayed = false;
        _playedCards.Clear();
        _extraTurnsRemaining = 0;
    }

    public void ResetForNextTurn()
    {
        // Сбрасываем состояние для следующего хода того же игрока
        _cardsPlayedThisTurn = 0;
        _hasDrawnCard = false;
        _turnEnded = false;
        _skipPlayed = false;
        _attackPlayed = false;
        _playedCards.Clear();
    }

    public async Task CompleteTurnAsync()
    {
        if (!_turnEnded)
        {
            if (MustDrawCard())
            {
                throw new InvalidOperationException("Игрок должен взять карту перед завершением хода!");
            }

            _turnEnded = true;
        }

        if (_attackPlayed)
        {
            ResetPlayerExtraTurns();
            _session.NextPlayer();

            if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
            {
                await _session.BroadcastMessage($"⚔️ {_session.CurrentPlayer.Name} ходит дважды из-за атаки!");
            }

            Reset();
            return;
        }

        // Проверяем, есть ли у текущего игрока дополнительный ход 
        if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
        {
            _session.CurrentPlayer.ExtraTurns--;

            ResetForNextTurn();

            await _session.BroadcastMessage($"🎮 {_session.CurrentPlayer.Name} продолжает ход (атака)!");
            await _session.CurrentPlayer.Connection.SendMessage("У вас дополнительный ход из-за атаки! Вы можете сыграть карту или взять карту из колоды.");

            return; // Не переходим к следующему игроку
        }

        ResetPlayerExtraTurns();
        _session.NextPlayer();

        if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
        {
            await _session.BroadcastMessage($"⚔️ {_session.CurrentPlayer.Name} ходит дважды из-за атаки!");
        }

        Reset();
    }

    public void ResetPlayerExtraTurns()
    {
        if (_session.CurrentPlayer != null)
        {
            _session.CurrentPlayer.ExtraTurns = 0;
        }
    }
    public bool HasDrawnCard => _hasDrawnCard;
    public bool TurnEnded => _turnEnded;
    public bool SkipPlayed => _skipPlayed;
    public bool AttackPlayed => _attackPlayed;
    public bool MustDrawCardBeforeEnd => MustDrawCard();
}