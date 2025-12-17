// File: Client/PlayerHandUpdateHandler.cs
using Server.Game.Enums; // Для CardType, если ClientCardDto использует его
using Server.Game.Models;
using Server.Networking.Commands; // Для Command
using System.Text;
using System.Text.Json;

namespace Client.ClientHandlers;

[ClientCommand(Command.PlayerHandUpdate)]
public class PlayerHandUpdateHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);

        try
        {
            // Десериализуем в DTO, а не в Server.Game.Models.Card
            var dtoCards = JsonSerializer.Deserialize<List<ClientCardDto>>(json); // <-- Изменено
            if (dtoCards != null)
            {
                // Преобразуем DTO в Client-side Card (если нужна внутренняя модель)
                // или работаем напрямую с DTO.
                // Вариант 1: Работаем с DTO
                var clientSideCards = dtoCards.Select(dto => new Card
                {
                    Type = dto.Type,
                    Name = dto.Name,
                    Description = "Описание недоступно", // или получить из словаря по типу
                    IconId = 0 // или получить из словаря по типу
                    // Установите значения по умолчанию или получите из Card.Create(dto.Type)
                }).ToList();

                // Вариант 2 (предпочтительнее): Используйте Card.Create для каждого типа
                var clientSideCardsFromFactory = dtoCards.Select(dto =>
                {
                    var fullCard = Card.Create(dto.Type);
                    // fullCard.Name уже будет правильным, но можно переписать, если DTO Name отличается
                    // fullCard.Description и fullCard.IconId будут правильными
                    return fullCard;
                }).ToList();

                client.Hand.Clear();
                client.Hand.AddRange(clientSideCardsFromFactory); // <-- Используем преобразованные карты

                client.AddToLog($"Обновлена рука. Карт: {client.Hand.Count}");

                // Показываем руку, если мало карт или произошли изменения
                if (client.Hand.Count <= 8)
                {
                    client.DisplayHand();
                }
            }
        }
        catch (JsonException ex)
        {
            client.AddToLog($"Ошибка разбора карт: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}