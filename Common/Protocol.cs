// Common/Protocol.cs
namespace Common
{
    public static class ExplodingKittensProtocol
    {
        // Команды клиента → серверу
        public const string CONNECT = "CONNECT";
        public const string DISCONNECT = "DISCONNECT";
        public const string START_GAME = "START_GAME";
        public const string PLAY_CARD = "PLAY_CARD";
        public const string PLAY_COMBO = "PLAY_COMBO";
        public const string DRAW_CARD = "DRAW_CARD"; // Обязательное взятие в конце хода
        public const string END_TURN = "END_TURN";
        public const string PLAY_NOPE = "PLAY_NOPE";
        public const string DEFUSE_KITTEN = "DEFUSE_KITTEN";
        public const string SELECT_TARGET = "SELECT_TARGET";
        public const string REQUEST_CARD = "REQUEST_CARD"; // Для комбо из 3
        public const string TAKE_FROM_DISCARD = "TAKE_DISCARD"; // Для комбо из 5
        public const string SEND_CARD_CHOICE = "SEND_CARD_CHOICE";       // Клиент → серверу: выбрал карту
        public const string SEND_CARD_SELECTION = "SEND_CARD_SELECTION";


        // Команды сервера → клиенту
        public const string GAME_STARTED = "GAME_STARTED";
        public const string GAME_UPDATE = "GAME_UPDATE";
        public const string TURN_CHANGED = "TURN_CHANGED";
        public const string CARD_PLAYED = "CARD_PLAYED";
        public const string KITTEN_DRAWN = "KITTEN_DRAWN";
        public const string NEED_DEFUSE = "NEED_DEFUSE";
        public const string PLAYER_ELIMINATED = "PLAYER_ELIMINATED";
        public const string GAME_OVER = "GAME_OVER";
        public const string REQUEST_TARGET = "REQUEST_TARGET";
        public const string SHOW_FUTURE = "SHOW_FUTURE";
        public const string COMBO_RESULT = "COMBO_RESULT";
        public const string ERROR = "ERROR";
        public const string NEED_CARD_CHOICE = "NEED_CARD_CHOICE"; // Запрос выбора карты для Favor
        public const string REQUEST_CARD_CHOICE = "REQUEST_CARD_CHOICE"; // Для выбора карты в Favor
        public const string REQUEST_CARD_SELECTION = "REQUEST_CARD_SELECTION";


        // Статусы игры
        public const string STATUS_WAITING = "WAITING";
        public const string STATUS_PLAYING = "PLAYING";
        public const string STATUS_FINISHED = "FINISHED";
    }
}