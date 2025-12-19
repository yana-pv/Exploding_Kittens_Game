namespace Shared.Models;

public enum CardType : byte
{
    // Взрывные котята
    ExplodingKitten = 0x10,

    // Защитные карты
    Defuse = 0x11,
    Nope = 0x12,

    // Карты действий
    Attack = 0x20,
    Skip = 0x21,
    Favor = 0x22,
    Shuffle = 0x23,
    SeeTheFuture = 0x24,

    // Карты котов (для комбо)
    RainbowCat = 0x30,
    BeardCat = 0x31,
    PotatoCat = 0x32,
    WatermelonCat = 0x33,
    TacoCat = 0x34
}

