using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.UseCombo)]
public class UseComboHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, // <-- Изменено
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var data = Encoding.UTF8.GetString(payload);
        var parts = data.Split(':');

        if (parts.Length < 4 || !Guid.TryParse(parts[0], out var gameId) ||
            !Guid.TryParse(parts[1], out var playerId) ||
            !int.TryParse(parts[2], out var comboType))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // Получаем сессию напрямую из менеджера
        var session = sessionManager.GetSession(gameId); // <-- Изменено
        if (session == null) // <-- Изменено условие
        {
            await sender.SendError(CommandResponse.GameNotFound);
            return;
        }

        var player = session.GetPlayerById(playerId);
        if (player == null || player.Connection != sender)
        {
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        if (session.State != GameState.PlayerTurn || session.CurrentPlayer != player)
        {
            await sender.SendError(CommandResponse.NotYourTurn);
            return;
        }

        // Парсим индексы карт
        var cardIndices = parts[3].Split(',')
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();

        // Проверяем валидность комбо
        if (!ValidateCombo(player, comboType, cardIndices))
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        try
        {
            // Обрабатываем комбо в зависимости от типа
            switch (comboType)
            {
                case 2: // Две одинаковые - слепой карманник
                    await HandleTwoOfAKind(session, player, cardIndices, parts.Length > 4 ? parts[4] : null);
                    break;

                case 3: // Три одинаковые - время рыбачить
                    await HandleThreeOfAKind(session, player, cardIndices, parts.Length > 4 ? parts[4] : null);
                    break;

                case 5: // Пять разных - воруй из колоды сброса
                    await HandleFiveDifferent(session, player, cardIndices);
                    break;

                default:
                    await sender.SendError(CommandResponse.InvalidAction);
                    return;
            }

            // Убираем использованные карты из руки и кладем в сброс
            var cardsToDiscard = cardIndices
                .OrderByDescending(i => i)
                .Select(i => player.Hand[i])
                .ToList();

            foreach (var card in cardsToDiscard)
            {
                player.Hand.Remove(card);
                session.GameDeck.Discard(card);
            }

            // Обновляем состояние
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();

            await session.BroadcastMessage($"{player.Name} использовал комбо!");
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при использовании комбо: {ex.Message}");
        }
    }

    private bool ValidateCombo(Player player, int comboType, List<int> cardIndices)
    {
        if (cardIndices.Count != comboType)
            return false;

        if (cardIndices.Any(i => i < 0 || i >= player.Hand.Count))
            return false;

        var cards = cardIndices.Select(i => player.Hand[i]).ToList();

        switch (comboType)
        {
            case 2: // Две одинаковые карты или с одинаковыми иконками
                return cards[0].Type == cards[1].Type ||
                       cards[0].IconId == cards[1].IconId;

            case 3: // Три одинаковые карты или с одинаковыми иконками
                return (cards[0].Type == cards[1].Type &&
                        cards[1].Type == cards[2].Type) ||
                       (cards[0].IconId == cards[1].IconId &&
                        cards[1].IconId == cards[2].IconId);

            case 5: // Пять разных карт с разными иконками
                return cards.Select(c => c.IconId).Distinct().Count() == 5;

            default:
                return false;
        }
    }

    private async Task HandleTwoOfAKind(GameSession session, Player player, List<int> cardIndices, string? targetPlayerId)
    {
        if (string.IsNullOrEmpty(targetPlayerId) || !Guid.TryParse(targetPlayerId, out var targetId))
        {
            await player.Connection.SendMessage("Укажите игрока для кражи карты!");
            throw new ArgumentException("Не указан целевой игрок");
        }

        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            await player.Connection.SendMessage("Некорректный целевой игрок!");
            throw new ArgumentException("Некорректный целевой игрок");
        }

        if (target.Hand.Count == 0)
        {
            await session.BroadcastMessage($"{target.Name} не имеет карт для кражи!");
            return;
        }

        // Крадем случайную карту
        var random = new Random();
        var stolenCardIndex = random.Next(target.Hand.Count);
        var stolenCard = target.Hand[stolenCardIndex];
        target.Hand.RemoveAt(stolenCardIndex);
        player.AddToHand(stolenCard);

        await session.BroadcastMessage($"{player.Name} украл карту у {target.Name} используя комбо!");
    }

    private async Task HandleThreeOfAKind(GameSession session, Player player, List<int> cardIndices, string? targetData)
    {
        if (string.IsNullOrEmpty(targetData))
        {
            await player.Connection.SendMessage("Укажите игрока и название карты в формате: playerId|cardName!");
            throw new ArgumentException("Не указаны данные для целевой карты");
        }

        var parts = targetData.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var targetId))
        {
            await player.Connection.SendMessage("Некорректный формат данных!");
            throw new ArgumentException("Некорректный формат данных");
        }

        var target = session.GetPlayerById(targetId);
        if (target == null || target == player || !target.IsAlive)
        {
            await player.Connection.SendMessage("Некорректный целевой игрок!");
            throw new ArgumentException("Некорректный целевой игрок");
        }

        var requestedCardName = parts[1];

        // Ищем карту по имени у целевого игрока
        var requestedCard = target.Hand.FirstOrDefault(c =>
            c.Name.Equals(requestedCardName, StringComparison.OrdinalIgnoreCase));

        if (requestedCard == null)
        {
            await session.BroadcastMessage($"{player.Name} пытался взять карту '{requestedCardName}' у {target.Name}, но такой карты нет!");
            return;
        }

        // Забираем карту
        target.Hand.Remove(requestedCard);
        player.AddToHand(requestedCard);

        await session.BroadcastMessage($"{player.Name} взял карту '{requestedCard.Name}' у {target.Name} используя комбо!");
    }

    private async Task HandleFiveDifferent(GameSession session, Player player, List<int> cardIndices)
    {
        if (session.GameDeck.DiscardPile.Count == 0)
        {
            await session.BroadcastMessage("Колода сброса пуста!");
            return;
        }

        // Показываем карты в сбросе игроку
        var discardCards = session.GameDeck.DiscardPile
            .Select((card, index) => $"{index}. {card.Name}")
            .ToList();

        var discardInfo = string.Join("\n", discardCards);
        await player.Connection.SendMessage($"Карты в сбросе:\n{discardInfo}");
        await player.Connection.SendMessage("Введите номер карты, которую хотите взять:");

        // В реальной реализации нужно дождаться ответа игрока
        // Для примепа берем первую карту из сброса
        if (session.GameDeck.DiscardPile.Count > 0)
        {
            var takenCard = session.GameDeck.TakeFromDiscard(0);
            player.AddToHand(takenCard);

            await session.BroadcastMessage($"{player.Name} взял карту '{takenCard.Name}' из колоды сброса используя комбо!");
        }
    }
}