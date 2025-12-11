using Server.Game.Enums;
using Server.Game.Models;
using Server.Infrastructure; // Добавлено
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Server.Networking.Commands.Handlers;

[Command(Command.PlayDefuse)]
public class PlayDefuseHandler : ICommandHandler
{
    private static readonly ConcurrentDictionary<Guid, Player> _pendingExplosions = new();

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

        if (parts.Length < 3 || !Guid.TryParse(parts[0], out var gameId) ||
            !Guid.TryParse(parts[1], out var playerId))
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

        // Проверяем, есть ли у игрока активный взрывной котенок
        if (!_pendingExplosions.ContainsKey(player.Id) || _pendingExplosions[player.Id] != player)
        {
            await player.Connection.SendMessage("У вас нет активного взрывного котенка для обезвреживания!");
            return;
        }

        // Проверяем, есть ли у игрока карта Defuse
        if (!player.HasDefuseCard)
        {
            await player.Connection.SendMessage("У вас нет карты Обезвредить!");
            await HandlePlayerElimination(session, player);
            return;
        }

        // Парсим позицию для возврата котенка в колоду
        if (!int.TryParse(parts[2], out var position) || position < 0)
        {
            position = 0; // По умолчанию кладем наверх
        }

        try
        {
            // Находим взрывного котенка в руке игрока
            var explodingKitten = player.Hand.FirstOrDefault(c => c.Type == CardType.ExplodingKitten);
            if (explodingKitten == null)
            {
                await player.Connection.SendMessage("Взрывной котенок не найден в вашей руке!");
                return;
            }

            // Убираем карту Defuse из руки
            var defuseCard = player.RemoveCard(CardType.Defuse);
            if (defuseCard == null)
            {
                await player.Connection.SendMessage("Не удалось найти карту Обезвредить!");
                return;
            }

            // Убираем взрывного котенка из руки
            player.Hand.Remove(explodingKitten);

            // Кладем Defuse в сброс
            session.GameDeck.Discard(defuseCard);

            // Возвращаем взрывного котенка в колоду на указанную позицию
            session.GameDeck.InsertCard(explodingKitten, position);

            // Очищаем информацию о pending взрыве
            _pendingExplosions.TryRemove(player.Id, out _);

            await session.BroadcastMessage($"✅ {player.Name} обезвредил Взрывного Котенка!");
            await session.BroadcastMessage($"{player.Name} вернул котенка в колоду на позицию {position} от верха.");

            // Продолжаем ход игрока (он вытянул карту, теперь может играть)
            await player.Connection.SendMessage("Котенок обезврежен! Ваш ход продолжается.");

            // Обновляем руку игрока
            await player.Connection.SendPlayerHand(player);
            await session.BroadcastGameState();
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при обезвреживании котенка: {ex.Message}");
        }
    }

    public static void RegisterExplosion(Player player)
    {
        _pendingExplosions[player.Id] = player;
    }

    public static bool HasPendingExplosion(Player player)
    {
        return _pendingExplosions.ContainsKey(player.Id);
    }

    private async Task HandlePlayerElimination(GameSession session, Player player)
    {
        // Игрок выбывает
        session.EliminatePlayer(player);

        // Очищаем информацию о взрыве
        _pendingExplosions.TryRemove(player.Id, out _);

        await session.BroadcastMessage($"💥 {player.Name} не смог обезвредить котенка и выбывает из игры!");

        // Переход к следующему игроку
        session.NextPlayer();
        if (session.State != GameState.GameOver)
        {
            await session.BroadcastMessage($"Ходит {session.CurrentPlayer!.Name}");
            await session.CurrentPlayer!.Connection.SendMessage("Ваш ход!");
        }

        await session.BroadcastGameState();
    }
}