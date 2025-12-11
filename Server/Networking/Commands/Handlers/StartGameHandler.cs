// File: Server/Networking/Commands/Handlers/StartGameHandler.cs
using Server.Game.Enums;
using Server.Game.Models; // Добавлено для доступа к Card и GameStateInfo
using Server.Infrastructure; // Добавлено
using Server.Networking.Protocol; // Добавлено для доступа к KittensPackageBuilder
using System.Net.Sockets;
using System.Text;
using System.Text.Json; // Добавлено для ручной проверки размера JSON

namespace Server.Networking.Commands.Handlers;

[Command(Command.StartGame)]
public class StartGameHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager, // <-- Изменено
        byte[]? payload = null, CancellationToken ct = default)
    {
        if (payload == null || payload.Length == 0)
        {
            await sender.SendError(CommandResponse.InvalidAction);
            return;
        }

        if (!Guid.TryParse(Encoding.UTF8.GetString(payload), out var gameId))
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

        var player = session.GetPlayerBySocket(sender);
        if (player == null)
        {
            await sender.SendError(CommandResponse.PlayerNotFound);
            return;
        }

        if (session.State != GameState.WaitingForPlayers)
        {
            await sender.SendError(CommandResponse.GameAlreadyStarted);
            return;
        }

        if (!session.CanStart)
        {
            await sender.SendError(CommandResponse.NotEnoughCards);
            return;
        }

        try
        {
            session.StartGame();

            // 1. Отправляем сообщение о начале игры (опционально, но логично)
            await session.BroadcastMessage($"Игра началась! Первым ходит {session.CurrentPlayer!.Name}");
            await session.CurrentPlayer!.Connection.SendMessage("Ваш ход! Вы можете сыграть карту или взять карту из колоды.");

            // 2. Отправляем каждому игроку его собственную руку
            //    Предварительно проверим размер JSON руки.
            bool handSendSuccess = true;
            foreach (var p in session.Players)
            {
                try
                {
                    // Проверяем размер JSON руки перед отправкой
                    var handJson = JsonSerializer.Serialize(p.Hand);
                    var handJsonBytes = Encoding.UTF8.GetBytes(handJson);
                    if (handJsonBytes.Length > KittensPackageMeta.MaxPayloadSize)
                    {
                        Console.WriteLine($"Предупреждение: Рука игрока {p.Name} превышает MaxPayloadSize ({handJsonBytes.Length} > {KittensPackageMeta.MaxPayloadSize}).");
                        // Возможно, стоит оптимизировать Card перед сериализацией или обрезать.
                        // Пока отправим, но это может вызвать ошибку в KittensPackageBuilder.
                        // Лучше оптимизировать Card.
                    }

                    // Отправляем руку через существующий метод
                    await p.Connection.SendPlayerHand(p);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки руки игроку {p.Name}: {ex.Message}");
                    handSendSuccess = false;
                    break; // Прерываем цикл, если одна рука не отправилась
                }
            }

            if (!handSendSuccess)
            {
                await sender.SendMessage("Ошибка при отправке начальных карт.");
                return; // Не продолжаем, если руки не отправились
            }

            // 3. Отправляем обновлённое состояние игры всем игрокам
            //    Предварительно проверим размер JSON состояния.
            try
            {
                var gameStateJson = session.GetGameStateJson();
                var gameStateJsonBytes = Encoding.UTF8.GetBytes(gameStateJson);
                if (gameStateJsonBytes.Length > KittensPackageMeta.MaxPayloadSize)
                {
                    Console.WriteLine($"Предупреждение: Состояние игры превышает MaxPayloadSize ({gameStateJsonBytes.Length} > {KittensPackageMeta.MaxPayloadSize}).");
                    // Оптимизировать GameStateInfo перед сериализацией.
                    // Пока отправим, но это может вызвать ошибку в KittensPackageBuilder.
                    // Лучше оптимизировать GameStateInfo.
                }

                // Отправляем состояние через существующий метод
                await session.BroadcastGameState(); // Этот метод, скорее всего, вызывает SendGameState для всех
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки состояния игры: {ex.Message}");
                await sender.SendMessage($"Ошибка при отправке состояния игры: {ex.Message}");
                // Важно: игра уже начата, но состояние не отправлено. Это может привести к рассинхрону.
                // В реальной игре нужно обработать это более гибко.
                return;
            }

            // Сообщение об успешном старте можно отправить игроку, вызвавшему start,
            // или оставить как есть, если остальные получили руки и состояние.
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка при запуске игры: {ex.Message}");
        }
    }
}