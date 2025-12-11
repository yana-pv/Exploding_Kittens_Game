using Server.Game.Enums;
using Server.Game.Models;
using Server.Networking.Commands;
using Server.Networking.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server.Infrastructure;

public class EKServer
{
    private readonly Socket _serverSocket;
    private readonly GameSessionManager _sessionManager = new();
    private readonly ConcurrentDictionary<Socket, Task> _clientTasks = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public EKServer(IPEndPoint endPoint)
    {
        _serverSocket = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);
        _serverSocket.Bind(endPoint);
        _serverSocket.Listen(100);

        Console.WriteLine($"Сервер запущен на {endPoint}");
    }

    public async Task StartAsync()
    {
        Console.WriteLine("Сервер запущен. Ожидание подключений...");

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var clientSocket = await _serverSocket.AcceptAsync(_cancellationTokenSource.Token);
                Console.WriteLine($"Новое подключение: {clientSocket.RemoteEndPoint}");

                // Запускаем обработку клиента в отдельной задаче
                var clientTask = Task.Run(() => HandleClientAsync(clientSocket),
                    _cancellationTokenSource.Token);

                _clientTasks[clientSocket] = clientTask;

                // Удаляем завершенные задачи
                CleanupCompletedTasks();
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Сервер остановлен.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сервера: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        _cancellationTokenSource.Cancel();

        // Ждем завершения всех клиентских задач
        await Task.WhenAll(_clientTasks.Values);

        _serverSocket.Close();
        Console.WriteLine("Сервер остановлен.");
    }

    private async Task HandleClientAsync(Socket clientSocket)
    {
        try
        {
            // Приветственное сообщение
            await SendWelcomeMessage(clientSocket);

            byte[] buffer = new byte[1024];

            while (!_cancellationTokenSource.Token.IsCancellationRequested &&
                   clientSocket.Connected)
            {
                var bytesReceived = await clientSocket.ReceiveAsync(buffer, SocketFlags.None,
                    _cancellationTokenSource.Token);

                if (bytesReceived == 0)
                {
                    Console.WriteLine($"Клиент отключился: {clientSocket.RemoteEndPoint}");
                    break;
                }

                // Копируем данные в новый массив нужного размера
                var data = new byte[bytesReceived];
                Array.Copy(buffer, 0, data, 0, bytesReceived);

                await ProcessClientData(clientSocket, data);
            }
        }
        catch (OperationCanceledException)
        {
            // Сервер остановлен
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        {
            Console.WriteLine($"Клиент разорвал соединение: {clientSocket.RemoteEndPoint}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка обработки клиента {clientSocket.RemoteEndPoint}: {ex.Message}");
        }
        finally
        {
            // Удаляем игрока из всех сессий
            await RemovePlayerFromAllSessions(clientSocket);

            clientSocket.Close();
            _clientTasks.TryRemove(clientSocket, out _);
        }
    }

    private async Task SendWelcomeMessage(Socket clientSocket)
    {
        var welcomeMessage = "Добро пожаловать в Взрывные Котята!\n" +
                            "Доступные команды:\n" +
                            "- create [имя] - создать новую игру\n" +
                            "- join [ID_игры] [имя] - присоединиться к игре\n" +
                            "- start [ID_игры] - начать игру\n" +
                            "- play [ID_игры] [номер_карты] - сыграть карту\n" +
                            "- draw [ID_игры] - взять карту из колоды\n";

        Console.WriteLine($"Отправляем сообщение длиной {Encoding.UTF8.GetByteCount(welcomeMessage)} байт");

        await clientSocket.SendAsync(KittensPackageBuilder.MessageResponse(welcomeMessage),
            SocketFlags.None);
    }

    private async Task ProcessClientData(Socket clientSocket, byte[] data)
    {
        Console.WriteLine($"Получено данных: {data.Length} байт");
        Console.WriteLine($"Hex: {BitConverter.ToString(data)}");

        var memory = new Memory<byte>(data);
        var parsed = KittensPackageParser.TryParse(memory.Span, out var error);

        if (parsed == null)
        {
            Console.WriteLine($"Ошибка парсинга: {error}");
            await SendErrorResponse(clientSocket, (CommandResponse)error!);
            return;
        }

        try
        {
            // Передаём _sessionManager вместо копии словаря
            var handler = CommandHandlerFactory.GetHandler(parsed.Value.Command);
            await handler.Invoke(clientSocket, _sessionManager, parsed.Value.Payload); // <-- Изменено
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка обработки команды: {ex.Message}");
            await SendMessageResponse(clientSocket, $"Ошибка: {ex.Message}");
        }
    }

    private async Task SendErrorResponse(Socket socket, CommandResponse error)
    {
        await socket.SendAsync(KittensPackageBuilder.ErrorResponse(error), SocketFlags.None);
    }

    private async Task SendMessageResponse(Socket socket, string message)
    {
        await socket.SendAsync(KittensPackageBuilder.MessageResponse(message), SocketFlags.None);
    }

    private async Task RemovePlayerFromAllSessions(Socket clientSocket)
    {
        foreach (var session in _sessionManager.GetActiveSessions()) // Это возвращает сессии НЕ в GameOver и с игроками
        {
            var player = session.GetPlayerBySocket(clientSocket);
            if (player != null)
            {
                session.RemovePlayer(player.Id);
                await BroadcastSessionMessage(session, $"{player.Name} отключился от игры.");

                // Проверяем, нужно ли завершить игру ИЛИ удалить сессию
                if (session.State != GameState.WaitingForPlayers) // Если игра уже началась
                {
                    if (session.AlivePlayersCount < session.MinPlayers && session.State != GameState.GameOver)
                    {
                        await BroadcastSessionMessage(session, "Игра прервана из-за недостатка игроков.");
                        session.State = GameState.GameOver; // Завершаем игру, она не будет возвращена через GetActiveSessions
                    }
                }
                else // Если игра ЕЩЁ не началась (WaitingForPlayers)
                {
                    // Если после удаления игрока в сессии никого не осталось
                    if (session.Players.Count == 0)
                    {
                        // Удаляем сессию из менеджера, чтобы она не мешалась и не накапливалась
                        _sessionManager.RemoveSession(session.Id);
                        Console.WriteLine($"Игра {session.Id} удалена, так как создатель отключился и игроков больше нет.");
                        // Больше ничего делать не нужно, сессия исчезнет из _sessionManager
                        // и не будет возвращена GetActiveSessions в следующий раз.
                    }
                    // Если в сессии остались другие игроки, она просто остаётся в ожидании.
                }
            }
        }
    }

    private async Task BroadcastSessionMessage(GameSession session, string message)
    {
        var messageData = KittensPackageBuilder.MessageResponse(message);
        foreach (var player in session.Players.Where(p => p.IsAlive || session.State == GameState.WaitingForPlayers))
        {
            await player.Connection.SendAsync(messageData, SocketFlags.None);
        }
    }

    private void CleanupCompletedTasks()
    {
        var completedTasks = _clientTasks
            .Where(kv => kv.Value.IsCompleted)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var socket in completedTasks)
        {
            _clientTasks.TryRemove(socket, out _);
        }
    }

    private ConcurrentDictionary<Guid, GameSession> GetSessionsDictionary()
    {
        // Конвертируем GameSessionManager в ConcurrentDictionary для совместимости
        var dict = new ConcurrentDictionary<Guid, GameSession>();
        foreach (var session in _sessionManager.GetActiveSessions())
        {
            dict.TryAdd(session.Id, session);
        }
        return dict;
    }
}