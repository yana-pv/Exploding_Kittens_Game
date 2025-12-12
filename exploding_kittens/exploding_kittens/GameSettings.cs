using System;

namespace exploding_kittens
{
    public static class GameSettings
    {
        public static string PlayerName { get; set; } = "Игрок1";
        public static int PlayersCount { get; set; } = 4;
        public static string ServerIP { get; set; } = "127.0.0.1:7777";
        public static Guid GameId { get; set; }
        public static Guid PlayerId { get; set; }
    }
}