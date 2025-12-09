// Common/GameModels.cs
namespace Common
{
    public class GameStartInfo
    {
        public Dictionary<string, List<Card>> PlayerHands { get; set; } = new();
        public List<Card> Deck { get; set; } = new();
        public string FirstPlayer { get; set; } = "";
        public int PlayersCount { get; set; }
        public int DefusesInDeck { get; set; }
    }

    public class GameUpdate
    {
        public string CurrentPlayer { get; set; } = "";
        public List<Card> DiscardPile { get; set; } = new();
        public int DeckCount { get; set; }
        public Dictionary<string, int> PlayerCardCounts { get; set; } = new();
        public string LastAction { get; set; } = "";
        public bool CanPlayNope { get; set; }
        public bool MustDrawCard { get; set; }
        public bool IsMyTurn { get; set; }
        public List<string> AlivePlayers { get; set; } = new();

        // Для специальных действий
        public bool NeedTargetPlayer { get; set; }
        public string ActionType { get; set; } = ""; // "FAVOR", "STEAL", etc.
        public List<Card> FutureCards { get; set; } = new();
        public bool CanTakeFromDiscard { get; set; }
        public long UpdateSequenceNumber { get; set; } = 0;
    }


    public class CardPlayRequest
    {
        public Card Card { get; set; } = new();
        public List<Card> ComboCards { get; set; } = new();
        public string TargetPlayer { get; set; } = "";
        // Убрал CardColor? SelectedColor - не используется в Взрывных котятах
        public int? KittenPlacement { get; set; } // Позиция для размещения котёнка (0-вверх, 1-вниз, 2-случайно)
        public string RequestedCardName { get; set; } = ""; // Для комбо из 3 карт
    }

    public class CardPlayResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public Card? DrawnCard { get; set; }
        public bool IsKitten { get; set; }
        public bool Defused { get; set; }
        public bool GameOver { get; set; }
        public string Winner { get; set; } = "";
        public List<Card> CardsToAdd { get; set; } = new(); // Для кражи/получения карт

        // ДОБАВИТЬ: Для карт которые нужно только показать (не добавлять в руку)
        public bool IsSeeTheFuture { get; set; }
        public bool WaitingForCardChoice { get; set; } // Для Favor: ожидание выбора карты
    }

    public class PlayerInfo
    {
        public string Nickname { get; set; } = "";
        public int CardsInHand { get; set; }
        public bool IsAlive { get; set; }
        public bool IsCurrent { get; set; }
    }
}