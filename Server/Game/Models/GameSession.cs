using Server.Game.Models.Actions;
using Server.Game.Services;
using Shared.Models;
using Shared.Protocol;
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

        return true;
    }

    public void StartGame()
    {
        if (!CanStart)
            throw new InvalidOperationException($"Необходимо {MinPlayers}-{MaxPlayers} игроков");

        GameDeck = new Deck();
        CardCounter = new CardCounter();

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
    }

    public void NextPlayer(bool force = false)
    {
        if (CurrentPlayer == null) return;

        if (!force && !TurnManager.TurnEnded)
        {
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
    }

    public async Task BroadcastToOthers(Player excludedPlayer, string message)
    {
        var messageData = KittensPackageBuilder.MessageResponse(message);
        foreach (var player in Players.Where(p => p.IsAlive && p != excludedPlayer && p.Connection != null))
        {
            await player.Connection.SendAsync(messageData, SocketFlags.None);
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

        SendEliminationMessageToPlayer(player);

        if (CurrentPlayer == player)
        {
            NextPlayer(true);
        }

        CheckGameOver();
    }

    private async void SendEliminationMessageToPlayer(Player player)
    {
        var message = "💥 Вы выбыли из игры!";
        var data = KittensPackageBuilder.MessageResponse(message);

        if (player.Connection != null && player.Connection.Connected)
        {
            try
            {
                await player.Connection.SendAsync(data, SocketFlags.None);
            }
            catch (Exception ex)
            {
            }
        }
    }
    private void CheckGameOver()
    {
        var alivePlayers = Players.Where(p => p.IsAlive).ToList();

        if (alivePlayers.Count == 1)
        {
            Winner = alivePlayers[0];
            State = GameState.GameOver;

            SendWinMessageToWinner(Winner);

            SendLoseMessageToOthers(alivePlayers[0]);
        }
        else if (alivePlayers.Count == 0)
        {
            State = GameState.GameOver;

            SendNoWinnerMessage();
        }
    }

    private async void SendWinMessageToWinner(Player winner)
    {
        var message = "🎉 ПОБЕДА! Вы выиграли игру!";
        var data = KittensPackageBuilder.MessageResponse(message);
        await winner.Connection.SendAsync(data, SocketFlags.None);
    }

    private async void SendLoseMessageToOthers(Player winner)
    {
        var message = $"🏆 Игра окончена! Победитель: {winner.Name}";
        var data = KittensPackageBuilder.MessageResponse(message);

        foreach (var player in Players.Where(p => p != winner && p.Connection != null))
        {
            await player.Connection.SendAsync(data, SocketFlags.None);
        }
    }

    private async void SendNoWinnerMessage()
    {
        var message = "🏁 Игра окончена! Нет победителей.";
        var data = KittensPackageBuilder.MessageResponse(message);

        foreach (var player in Players.Where(p => p.Connection != null))
        {
            await player.Connection.SendAsync(data, SocketFlags.None);
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
}
