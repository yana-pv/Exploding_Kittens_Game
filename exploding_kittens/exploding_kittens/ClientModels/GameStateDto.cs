using System;
using System.Collections.Generic;

namespace exploding_kittens.ClientModels
{
    public class GameStateDto
    {
        public Guid SessionId { get; set; }
        public GameState State { get; set; }
        public string CurrentPlayerName { get; set; }
        public int AlivePlayers { get; set; }
        public int CardsInDeck { get; set; }
        public int TurnsPlayed { get; set; }
        public string WinnerName { get; set; }
        public List<PlayerInfoDto> Players { get; set; }

        public GameStateDto()
        {
            Players = new List<PlayerInfoDto>();
        }
    }

    public class PlayerInfoDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int CardCount { get; set; }
        public bool IsAlive { get; set; }
        public int TurnOrder { get; set; }
        public bool IsCurrentPlayer { get; set; }

        public PlayerInfoDto()
        {
            Name = string.Empty;
        }
    }

    public enum GameState
    {
        WaitingForPlayers,
        Initializing,
        PlayerTurn,
        WaitingForNope,
        ResolvingAction,
        GameOver,
        Paused
    }
}