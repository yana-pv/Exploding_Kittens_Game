// Common/CardTypes.cs
using System.Text.Json.Serialization;

namespace Common
{
    public enum CardType : byte
    {
        ExplodingKitten = 0,    // Взрывной котёнок
        Defuse = 1,             // Обезвредить
        Attack = 2,             // Атаковать
        Skip = 3,               // Пропустить
        Favor = 4,              // Одолжить
        Shuffle = 5,            // Перемешать
        SeeTheFuture = 6,       // Заглянуть в будущее
        Nope = 7,               // Нет
        TacoCat = 8,            // Такокот
        PotatoCat = 9,          // Картофелекот
        BeardCat = 10,          // Бородакот
        RainbowCat = 11,        // Радужнокот
        CaticornCat = 12        // Единорожекот
    }

    public enum CardCategory : byte
    {
        Kitten = 0,
        Action = 1,
        Cat = 2
    }

    public class Card
    {
        public int Id { get; set; }
        public CardType Type { get; set; }
        public CardCategory Category { get; set; }
        public string Icon { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        [JsonIgnore]
        public bool IsDefused { get; set; }

        [JsonIgnore]
        public bool CanBePlayedAnytime => Type == CardType.Nope;

        public byte[] ToNetworkBytes()
        {
            var bytes = new byte[4];
            bytes[0] = (byte)Type;
            bytes[1] = (byte)Category;
            bytes[2] = (byte)(Id >> 8);
            bytes[3] = (byte)Id;
            return bytes;
        }

        public static Card FromNetworkBytes(byte[] bytes)
        {
            return new Card
            {
                Type = (CardType)bytes[0],
                Category = (CardCategory)bytes[1],
                Id = (bytes[2] << 8) | bytes[3]
            };
        }

        public override string ToString() => $"{Name} (ID: {Id})";
        public override bool Equals(object obj) => obj is Card other && Id == other.Id;
        public override int GetHashCode() => Id.GetHashCode();
    }
}