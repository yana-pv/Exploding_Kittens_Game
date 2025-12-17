using System.Text.Json.Serialization;

namespace Server.Game.Models;

public class Card
{
    public required CardType Type { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required byte IconId { get; set; }

    [JsonIgnore]
    public bool IsCatCard => Type >= CardType.RainbowCat && Type <= CardType.TacoCat;

    public static Card Create(CardType type)
    {
        return type switch
        {
            CardType.ExplodingKitten => new Card
            {
                Type = type,
                Name = "Взрывной Котенок",
                Description = "Если нет карты Обезвредить - вы проиграли!",
                IconId = 0
            },
            CardType.Defuse => new Card
            {
                Type = type,
                Name = "Обезвредить",
                Description = "Спасает от Взрывного Котенка",
                IconId = 1
            },
            CardType.Nope => new Card
            {
                Type = type,
                Name = "Нет",
                Description = "Отменяет действие карты (кроме Взрывного Котенка и Обезвредить)",
                IconId = 2
            },
            CardType.Attack => new Card
            {
                Type = type,
                Name = "Атаковать",
                Description = "Заканчивает ваш ход, следующий игрок ходит дважды",
                IconId = 3
            },
            CardType.Skip => new Card
            {
                Type = type,
                Name = "Пропустить",
                Description = "Заканчивает ход без взятия карты",
                IconId = 4
            },
            CardType.Favor => new Card
            {
                Type = type,
                Name = "Одолжение",
                Description = "Выбранный игрок отдает вам любую карту",
                IconId = 5
            },
            CardType.Shuffle => new Card
            {
                Type = type,
                Name = "Перемешать",
                Description = "Тщательно перемешивает колоду",
                IconId = 6
            },
            CardType.SeeTheFuture => new Card
            {
                Type = type,
                Name = "Заглянуть в будущее",
                Description = "Посмотреть 3 верхние карты колоды",
                IconId = 7
            },
            CardType.RainbowCat => new Card
            {
                Type = type,
                Name = "Радужный Кот",
                Description = "Карта кота для комбо",
                IconId = 8
            },
            CardType.BeardCat => new Card
            {
                Type = type,
                Name = "Котобородач",
                Description = "Карта кота для комбо",
                IconId = 9
            },
            CardType.PotatoCat => new Card
            {
                Type = type,
                Name = "Кошка-Картошка",
                Description = "Карта кота для комбо",
                IconId = 10
            },
            CardType.WatermelonCat => new Card
            {
                Type = type,
                Name = "Арбузный Котэ",
                Description = "Карта кота для комбо",
                IconId = 11
            },
            CardType.TacoCat => new Card
            {
                Type = type,
                Name = "Такокот",
                Description = "Карта кота для комбо",
                IconId = 12
            },
            _ => throw new ArgumentException($"Unknown card type: {type}")
        };
    }
}
