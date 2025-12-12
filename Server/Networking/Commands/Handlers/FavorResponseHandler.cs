using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayFavor)]
public class FavorResponseHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager,
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var data = Encoding.UTF8.GetString(payload);
        Console.WriteLine($"DEBUG FavorResponse: Получены данные: '{data}'");

        var parts = data.Split(':');

        // Проверяем минимальное количество частей: gameId:playerId:cardIndex
        if (parts.Length < 3)
        {
            Console.WriteLine($"DEBUG FavorResponse: Недостаточно частей ({parts.Length})");
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // Проверяем gameId
        if (!Guid.TryParse(parts[0], out var gameId))
        {
            Console.WriteLine($"DEBUG FavorResponse: Неверный gameId: '{parts[0]}'");
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        // Проверяем playerId
        if (!Guid.TryParse(parts[1], out var playerId))
        {
            Console.WriteLine($"DEBUG FavorResponse: Неверный playerId: '{parts[1]}'");
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        var session = sessionManager.GetSession(gameId);
        if (session == null)
        {
            Console.WriteLine($"DEBUG FavorResponse: Сессия не найдена: {gameId}");
            await sender.SendError(CommandResponse.GameNotFound);
            return;
        }

        var player = session.GetPlayerById(playerId);
        if (player == null || player.Connection != sender)
        {
            Console.WriteLine($"DEBUG FavorResponse: Игрок не найден или не совпадает соединение");
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        // Проверяем, есть ли pending favor для этого игрока
        if (session.PendingFavor == null)
        {
            Console.WriteLine($"DEBUG FavorResponse: Нет активного запроса на одолжение");
            await sender.SendError(CommandResponse.InvalidAction);
            await player.Connection.SendMessage("❌ Нет активного запроса на одолжение!");
            return;
        }

        if (session.PendingFavor.Target != player)
        {
            Console.WriteLine($"DEBUG FavorResponse: Этот запрос не для вас. Target: {session.PendingFavor.Target.Name}, Вы: {player.Name}");
            await sender.SendError(CommandResponse.InvalidAction);
            await player.Connection.SendMessage("❌ Этот запрос не для вас!");
            return;
        }

        // Проверяем cardIndex
        if (!int.TryParse(parts[2], out var cardIndex))
        {
            Console.WriteLine($"DEBUG FavorResponse: Неверный номер карты: '{parts[2]}'");
            await player.Connection.SendMessage($"❌ Неверный номер карты! Используйте число от 0 до {player.Hand.Count - 1}");
            await player.Connection.SendPlayerHand(player);
            return;
        }

        if (cardIndex < 0 || cardIndex >= player.Hand.Count)
        {
            Console.WriteLine($"DEBUG FavorResponse: Номер карты вне диапазона: {cardIndex}, карт в руке: {player.Hand.Count}");
            await player.Connection.SendMessage($"❌ Неверный номер карты! У вас только {player.Hand.Count} карт (0-{player.Hand.Count - 1})");
            await player.Connection.SendPlayerHand(player);
            return;
        }

        var favor = session.PendingFavor;
        var stolenCard = player.Hand[cardIndex];

        Console.WriteLine($"DEBUG FavorResponse: {player.Name} отдает карту #{cardIndex}: {stolenCard.Name} игроку {favor.Requester.Name}");

        // Убираем карту у целевого игрока
        player.Hand.RemoveAt(cardIndex);

        // Добавляем карту запрашивающему игроку
        favor.Requester.AddToHand(stolenCard);

        await session.BroadcastMessage($"✅ {favor.Requester.Name} взял карту '{stolenCard.Name}' у {player.Name}!");

        // Очищаем pending действие
        session.PendingFavor = null;
        session.State = GameState.PlayerTurn;

        // Обновляем руки обоих игроков
        await player.Connection.SendMessage($"📤 Вы отдали карту: {stolenCard.Name}");
        await player.Connection.SendPlayerHand(player);

        await favor.Requester.Connection.SendMessage($"📥 Вы получили карту: {stolenCard.Name}");
        await favor.Requester.Connection.SendPlayerHand(favor.Requester);

        await session.BroadcastGameState();

        Console.WriteLine($"DEBUG FavorResponse: Обмен завершен. У {player.Name} осталось {player.Hand.Count} карт, у {favor.Requester.Name} теперь {favor.Requester.Hand.Count} карт");
    }
}