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
    private bool _skipPlayed = false;   // Был ли сыгран Skip
    private bool _attackPlayed = false; // Был ли сыгран Attack
    private int _extraTurnsRemaining = 0; // Сколько дополнительных ходов осталось у текущего игрока

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

            // ВАЖНОЕ ИЗМЕНЕНИЕ: проверяем, является ли игрок жертвой предыдущей атаки
            if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
            {
                Console.WriteLine($"DEBUG CardPlayed: атакованный игрок {_session.CurrentPlayer.Name} контратакует!");
                // Жертва атаки контратакует - переносим атаку на следующего игрока

                // 1. Сбрасываем ExtraTurns у текущего игрока (он больше не атакован)
                _session.CurrentPlayer.ExtraTurns = 0;

                // 2. Помечаем СЛЕДУЮЩЕГО игрока как атакованного
                MarkNextPlayerAsAttacked();
            }
            else
            {
                // Обычная атака
                MarkNextPlayerAsAttacked();
            }
        }
    }

    private void TransferAttackToNextPlayer()
    {
        if (_session.CurrentPlayer == null)
            return;

        Console.WriteLine($"DEBUG TransferAttackToNextPlayer: перенос атаки от {_session.CurrentPlayer.Name}");

        // Сбрасываем ExtraTurns у текущего игрока
        _session.CurrentPlayer.ExtraTurns = 0;

        // Находим следующего живого игрока
        var currentIndex = _session.CurrentPlayerIndex;
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

        // Помечаем следующего игрока как атакованного
        var nextPlayer = players[currentIndex];
        nextPlayer.ExtraTurns = 1;

        Console.WriteLine($"DEBUG TransferAttackToNextPlayer: {nextPlayer.Name} теперь атакован, ExtraTurns={nextPlayer.ExtraTurns}");
    }

    private void MarkNextPlayerAsAttacked(Player fromPlayer = null)
    {
        var startPlayer = fromPlayer ?? _session.CurrentPlayer;
        if (startPlayer == null)
            return;

        // Находим следующего живого игрока после стартового игрока
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

        // Помечаем следующего игрока как "атакованного"
        var attackedPlayer = players[currentIndex];
        attackedPlayer.ExtraTurns = 1;

        Console.WriteLine($"DEBUG MarkNextPlayerAsAttacked: Игрок {attackedPlayer.Name} помечен как атакованный. ExtraTurns={attackedPlayer.ExtraTurns}");
    }

    public void CardDrawn()
    {
        _hasDrawnCard = true;

        Console.WriteLine($"DEBUG CardDrawn: игрок {_session.CurrentPlayer?.Name}, ExtraTurns={_session.CurrentPlayer?.ExtraTurns}");

        // Проверяем, нужно ли игроку ходить еще раз (если он атакован)
        if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
        {
            Console.WriteLine($"DEBUG CardDrawn: у игрока есть дополнительный ход");
            // У игрока есть дополнительный ход из-за атаки
            // НЕ завершаем ход, сбрасываем состояние для следующего хода
            _session.CurrentPlayer.ExtraTurns--;

            // Сбрасываем флаги для следующего хода (но сохраняем ExtraTurns)
            ResetForNextTurn();
        }
        else
        {
            Console.WriteLine($"DEBUG CardDrawn: обычное завершение хода");
            // Обычное завершение хода
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
        _extraTurnsRemaining = 0;

        // УДАЛИТЕ ЭТОТ КОД:
        // // Сбрасываем флаги дополнительных ходов у игрока
        // if (_session.CurrentPlayer != null)
        // {
        //     _session.CurrentPlayer.ExtraTurns = 0;
        // }
    }

    // ИЗМЕНЕНО: public вместо private
    public void ResetForNextTurn()
    {
        // Сбрасываем состояние для следующего хода того же игрока
        // (используется когда игрок ходит несколько раз подряд)
        _cardsPlayedThisTurn = 0;
        _hasDrawnCard = false;
        _turnEnded = false;
        _skipPlayed = false;
        _attackPlayed = false;
        _playedCards.Clear();
        // НЕ сбрасываем _extraTurnsRemaining - это глобальный счетчик

        Console.WriteLine($"DEBUG ResetForNextTurn: состояние сброшено для дополнительного хода");
    }

    // Метод для завершения хода и перехода к следующему игроку
    public async Task CompleteTurnAsync()
    {
        Console.WriteLine($"DEBUG CompleteTurnAsync: начало, turnEnded={_turnEnded}, CurrentPlayer={_session.CurrentPlayer?.Name}, ExtraTurns={_session.CurrentPlayer?.ExtraTurns}");

        if (!_turnEnded)
        {
            // Если ход еще не завершен, но должен быть
            if (MustDrawCard())
            {
                throw new InvalidOperationException("Игрок должен взять карту перед завершением хода!");
            }

            _turnEnded = true;
        }

        // Проверяем, был ли ход завершен картой Attack
        if (_attackPlayed)
        {
            Console.WriteLine($"DEBUG CompleteTurnAsync: ход завершен картой Attack");
            // Если ход завершен атакой, НЕ даем дополнительный ход, даже если есть ExtraTurns
            // Атака имеет приоритет - она немедленно завершает ход

            // Сбрасываем ExtraTurns у текущего игрока
            ResetPlayerExtraTurns();

            // Переходим к следующему игроку
            _session.NextPlayer();

            // Проверяем, атакован ли новый текущий игрок
            if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
            {
                await _session.BroadcastMessage($"⚔️ {_session.CurrentPlayer.Name} ходит дважды из-за атаки!");
            }

            // Сбрасываем состояние для нового хода
            Reset();
            return;
        }

        // Проверяем, есть ли у текущего игрока дополнительный ход (только если НЕ сыграли Attack)
        if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
        {
            Console.WriteLine($"DEBUG CompleteTurnAsync: у игрока {_session.CurrentPlayer.Name} есть дополнительный ход (ExtraTurns={_session.CurrentPlayer.ExtraTurns})");

            // Уменьшаем счетчик дополнительных ходов
            _session.CurrentPlayer.ExtraTurns--;

            // Сбрасываем состояние для следующего хода того же игрока
            ResetForNextTurn();

            await _session.BroadcastMessage($"🎮 {_session.CurrentPlayer.Name} продолжает ход (атака)!");
            await _session.CurrentPlayer.Connection.SendMessage("У вас дополнительный ход из-за атаки! Вы можете сыграть карту или взять карту из колоды.");

            return; // Не переходим к следующему игроку
        }

        Console.WriteLine($"DEBUG CompleteTurnAsync: переходим к следующему игроку");

        // Сбрасываем ExtraTurns у текущего игрока перед переходом
        ResetPlayerExtraTurns();

        // Переходим к следующему игроку
        _session.NextPlayer();

        // Проверяем, атакован ли новый текущий игрок
        if (_session.CurrentPlayer != null && _session.CurrentPlayer.ExtraTurns > 0)
        {
            await _session.BroadcastMessage($"⚔️ {_session.CurrentPlayer.Name} ходит дважды из-за атаки!");
        }

        // Сбрасываем состояние для нового хода
        Reset();
    }

    // ИЗМЕНЕНО: public вместо private
    public void ResetAttackFlag()
    {
        _attackPlayed = false;
        if (!_skipPlayed) // Если не сыграли Skip, ход не завершен
        {
            _turnEnded = false;
        }
        Console.WriteLine($"DEBUG ResetAttackFlag: _attackPlayed={_attackPlayed}, _turnEnded={_turnEnded}");
    }

    // Новый метод для проверки, был ли ход завершен картой
    public bool WasTurnEndedByCard()
    {
        return _skipPlayed || _attackPlayed;
    }

    public void ResetPlayerExtraTurns()
    {
        if (_session.CurrentPlayer != null)
        {
            _session.CurrentPlayer.ExtraTurns = 0;
        }
    }
    public int CardsPlayedCount => _cardsPlayedThisTurn;
    public bool HasDrawnCard => _hasDrawnCard;
    public bool TurnEnded => _turnEnded;
    public bool SkipPlayed => _skipPlayed;
    public bool AttackPlayed => _attackPlayed;
    public bool MustDrawCardBeforeEnd => MustDrawCard();

    // Новое свойство для отладки
    public int ExtraTurnsRemaining => _extraTurnsRemaining;
}