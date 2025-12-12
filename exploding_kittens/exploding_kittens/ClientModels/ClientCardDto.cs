using Newtonsoft.Json;

namespace exploding_kittens.ClientModels
{
    public class ClientCardDto
    {
        public CardType Type { get; set; }
        public string Name { get; set; } = string.Empty;

        [JsonIgnore]
        public string ImagePath
        {
            get
            {
                switch (Type)
                {
                    case CardType.ExplodingKitten:
                        return "photo/explodingkitten.png";
                    case CardType.Defuse:
                        return "photo/defuse1.png";
                    case CardType.Nope:
                        return "photo/nope1.png";
                    case CardType.Attack:
                        return "photo/attack1.png";
                    case CardType.Skip:
                        return "photo/skip1.png";
                    case CardType.Favor:
                        return "photo/favor.png";
                    case CardType.Shuffle:
                        return "photo/shuffle.png";
                    case CardType.SeeTheFuture:
                        return "photo/seethefuture.png";
                    case CardType.RainbowCat:
                        return "photo/rainbowcat.png";
                    case CardType.BeardCat:
                        return "photo/beardcat.png";
                    case CardType.PotatoCat:
                        return "photo/potatocat.png";
                    case CardType.WatermelonCat:
                        return "photo/watermeloncat.png";
                    case CardType.TacoCat:
                        return "photo/tacocat.png";
                    default:
                        return "photo/Shirt.png";
                }
            }
        }
    }

    public enum CardType : byte
    {
        ExplodingKitten = 0x10,
        Defuse = 0x11,
        Nope = 0x12,
        Attack = 0x20,
        Skip = 0x21,
        Favor = 0x22,
        Shuffle = 0x23,
        SeeTheFuture = 0x24,
        RainbowCat = 0x30,
        BeardCat = 0x31,
        PotatoCat = 0x32,
        WatermelonCat = 0x33,
        TacoCat = 0x34
    }
}