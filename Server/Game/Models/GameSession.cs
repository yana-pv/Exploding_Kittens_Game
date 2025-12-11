using Server.Game.Enums;
using Server.Game.Services;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Game.Models;

public class GameSession
{
    public required Guid Id { get; set; }
    public List<Player> Players { get; } = new();
    public required Deck GameDeck { get; set; }
    public Player? CurrentPlayer { get; set; }
    public int CurrentPlayerIndex { get; set; }
    public GameState State { get; set; } = GameState.WaitingForPlayers;
    public int MaxPlayers { get; set; } = 5;
    public int MinPlayers { get; set; } = 2;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Player? Winner { get; set; }
    public int TurnsPlayed { get; set; }
    public List<string> GameLog { get; } = new();
    public CardCounter CardCounter { get; private set; } = null!;


    // Новые поля
    [JsonIgnore]
    public TurnManager TurnManager { get; private set; } = null!;

    [JsonIgnore]
    public bool NeedsToDrawCard { get; set; }

    [JsonIgnore]
    public bool IsFull => Players.Count >= MaxPlayers;

    [JsonIgnore]
    public bool CanStart => Players.Count >= MinPlayers && Players.Count <= MaxPlayers;

    [JsonIgnore]
    public int AlivePlayersCount => Players.Count(p => p.IsAlive);

    // Действие, ожидающее карту "Нет"
    [JsonIgnore]
    public PendingAction? PendingNopeAction { get; set; }

    public void InitializeTurnManager()
    {
        TurnManager = new TurnManager(this);
    }

    public Player? GetPlayerById(Guid playerId)
    {
        return Players.FirstOrDefault(p => p.Id == playerId);
    }

    public Player? GetPlayerBySocket(Socket socket)
    {
        return Players.FirstOrDefault(p => p.Connection == socket);
    }

    public bool AddPlayer(Player player)
    {
        if (IsFull || State != GameState.WaitingForPlayers)
            return false;

        player.TurnOrder = Players.Count;
        Players.Add(player);

        Log($"{player.Name} присоединился к игре");

        return true;
    }

    public bool RemovePlayer(Guid playerId)
    {
        var player = GetPlayerById(playerId);
        if (player == null) return false;

        Players.Remove(player);

        if (State != GameState.WaitingForPlayers)
        {
            player.IsAlive = false;
            CheckGameOver();
        }

        Log($"{player.Name} покинул игру");

        return true;
    }

    public void StartGame()
    {
        if (!CanStart)
            throw new InvalidOperationException($"Необходимо {MinPlayers}-{MaxPlayers} игроков");

        GameDeck = new Deck();

        var cards = DeckInitializer.CreateDeckForPlayers(Players.Count);
        GameDeck.Initialize(cards);

        CardCounter = new CardCounter();
        CardCounter.Initialize(cards);

        foreach (var player in Players)
        {
            for (int i = 0; i < 4; i++)
            {
                player.AddToHand(GameDeck.Draw());
            }

            player.AddToHand(Card.Create(CardType.Defuse));
        }

        CurrentPlayerIndex = new Random().Next(Players.Count);
        CurrentPlayer = Players[CurrentPlayerIndex];
        State = GameState.PlayerTurn;
        TurnsPlayed = 0;

        InitializeTurnManager();

        Log($"Игра началась! Первым ходит {CurrentPlayer.Name}");
    }

    public void NextPlayer(bool force = false)
    {
        if (CurrentPlayer == null) return;

        // Если ход еще не закончен и не форсированно, ждем
        if (!force && !TurnManager.TurnEnded && !NeedsToDrawCard)
            return;

        // Завершаем текущий ход
        if (!TurnManager.TurnEnded)
        {
            TurnManager.EndTurn();
        }

        // Если нужно взять карту, но игрок еще не взял
        if (NeedsToDrawCard)
        {
            // Игрок должен взять карту перед сменой хода
            return;
        }

        int attempts = 0;
        do
        {
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
            CurrentPlayer = Players[CurrentPlayerIndex];
            attempts++;

            if (attempts > Players.Count)
            {
                State = GameState.GameOver;
                return;
            }
        }
        while (!CurrentPlayer.IsAlive);

        TurnsPlayed++;

        // Сброс состояния хода для нового игрока
        TurnManager = new TurnManager(this);
        NeedsToDrawCard = false;

        // Обработка дополнительных ходов
        if (CurrentPlayer.ExtraTurns > 0)
        {
            CurrentPlayer.ExtraTurns--;
            Log($"{CurrentPlayer.Name} имеет дополнительный ход");
        }
    }

    public void EliminatePlayer(Player player)
    {
        player.IsAlive = false;

        foreach (var card in player.Hand.Where(c => c.Type != CardType.ExplodingKitten))
        {
            GameDeck.InsertCard(card, new Random().Next(0, 5));
        }
        player.Hand.Clear();

        Log($"{player.Name} выбыл из игры!");

        // Если это текущий игрок, завершаем ход
        if (CurrentPlayer == player)
        {
            NextPlayer(true);
        }

        CheckGameOver();
    }

    private void CheckGameOver()
    {
        var alivePlayers = Players.Where(p => p.IsAlive).ToList();

        if (alivePlayers.Count == 1)
        {
            Winner = alivePlayers[0];
            State = GameState.GameOver;
            Log($"🎉 {Winner.Name} победил!");
        }
        else if (alivePlayers.Count == 0)
        {
            State = GameState.GameOver;
            Log("Игра окончена! Нет победителей.");
        }
    }

    public string GetGameStateJson()
    {
        var state = new ClientGameStateDto // <-- Используем DTO
        {
            SessionId = Id,
            State = State,
            CurrentPlayerName = CurrentPlayer?.Name,
            AlivePlayers = Players.Count(p => p.IsAlive),
            CardsInDeck = GameDeck.CardsRemaining
            // TurnsPlayed, WinnerName, NeedsToDraw и др. не включаем
        };

        return JsonSerializer.Serialize(state);
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        GameLog.Add($"[{timestamp}] {message}");

        if (GameLog.Count > 100)
            GameLog.RemoveAt(0);
    }

    // Новый метод для обработки карты "Атаковать"
    public void ApplyAttack(Player attacker, Player? target = null)
    {
        // Атака заканчивает ход текущего игрока БЕЗ взятия карты
        TurnManager.ForceEndTurn();
        NeedsToDrawCard = false;

        // Если указана цель, она получает дополнительный ход
        if (target != null && target.IsAlive)
        {
            target.ExtraTurns += 2;
            Log($"{attacker.Name} атаковал {target.Name}! {target.Name} ходит дважды.");
        }
        else
        {
            // Следующий игрок ходит дважды
            var nextPlayer = GetNextAlivePlayer();
            if (nextPlayer != null)
            {
                nextPlayer.ExtraTurns += 2;
                Log($"{attacker.Name} атаковал! {nextPlayer.Name} ходит дважды.");
            }
        }

        // Сразу переходим к следующему игроку
        NextPlayer(true);
    }

    private Player? GetNextAlivePlayer()
    {
        var index = CurrentPlayerIndex;
        var attempts = 0;

        do
        {
            index = (index + 1) % Players.Count;
            attempts++;

            if (attempts > Players.Count) return null;
        }
        while (!Players[index].IsAlive);

        return Players[index];
    }

    // Структура для ожидающих действий
    public class PendingAction
    {
        public required Player Player { get; set; }
        public required Card Card { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public object? ActionData { get; set; }
    }
}
