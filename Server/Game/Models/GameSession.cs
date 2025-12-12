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

    [JsonIgnore]
    public PendingFavorAction? PendingFavor { get; set; }

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
        CardCounter = new CardCounter();

        // Используем новый метод для создания игры
        var (finalDeck, playerHands) = DeckInitializer.CreateGameSetup(Players.Count);

        // Раздаем карты игрокам
        for (int i = 0; i < Players.Count; i++)
        {
            var player = Players[i];
            var hand = playerHands[i];

            foreach (var card in hand)
            {
                player.AddToHand(card);
            }
        }

        // Инициализируем колоду
        GameDeck.Initialize(finalDeck);
        CardCounter.Initialize(finalDeck);

        // Учитываем карты в руках игроков в счетчике
        foreach (var player in Players)
        {
            foreach (var card in player.Hand)
            {
                // Перемещаем карту из колоды в руку в счетчике
                CardCounter.CardMoved(card.Type, CardLocation.Deck, CardLocation.Hand);
            }
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

        // Используем TurnManager для проверки завершения хода
        if (!force && !TurnManager.TurnEnded)
        {
            // Ход еще не завершен
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
        // TurnManager сам сбросится в CompleteTurnAsync()
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
        var state = new ClientGameStateDto
        {
            SessionId = Id,
            State = State,
            CurrentPlayerName = CurrentPlayer?.Name,
            AlivePlayers = Players.Count(p => p.IsAlive),
            CardsInDeck = GameDeck.CardsRemaining,
            // Теперь эти свойства существуют:
            TurnsPlayed = TurnsPlayed,
            WinnerName = Winner?.Name,
            Players = Players.Select(p => new PlayerInfoDto
            {
                Id = p.Id,
                Name = p.Name,
                CardCount = p.Hand.Count,
                IsAlive = p.IsAlive,
                TurnOrder = p.TurnOrder,
                IsCurrentPlayer = CurrentPlayer?.Id == p.Id
            }).ToList()
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

    // ДОБАВЬТЕ ЭТОТ КЛАСС:
    public class PendingFavorAction
    {
        public required Player Requester { get; set; }
        public required Player Target { get; set; }
        public required Card Card { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
