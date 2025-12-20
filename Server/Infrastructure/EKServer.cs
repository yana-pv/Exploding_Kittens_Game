using Server.Game.Models;
using Server.Networking.Commands;
using Shared.Models;
using Shared.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

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

                var clientTask = Task.Run(() => HandleClientAsync(clientSocket),
                    _cancellationTokenSource.Token);

                _clientTasks[clientSocket] = clientTask;

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

    private async Task HandleClientAsync(Socket clientSocket)
    {
        try
        {
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

    private async Task ProcessClientData(Socket clientSocket, byte[] data)
    {
        var memory = new Memory<byte>(data);
        var parsed = KittensPackageParser.TryParse(memory.Span, out var error);

        if (parsed == null)
        {
            await SendErrorResponse(clientSocket, (CommandResponse)error!);
            return;
        }

        try
        {
            var handler = CommandHandlerFactory.GetHandler(parsed.Value.Command);
            await handler.Invoke(clientSocket, _sessionManager, parsed.Value.Payload); 
        }
        catch (Exception ex)
        {
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
        foreach (var session in _sessionManager.GetActiveSessions()) 
        {
            var player = session.GetPlayerBySocket(clientSocket);
            if (player != null)
            {
                session.RemovePlayer(player.Id);
                await BroadcastSessionMessage(session, $"{player.Name} отключился от игры.");

                // Проверяем, нужно ли завершить игру ИЛИ удалить сессию
                if (session.State != GameState.WaitingForPlayers) 
                {
                    if (session.AlivePlayersCount < session.MinPlayers && session.State != GameState.GameOver)
                    {
                        await BroadcastSessionMessage(session, "Игра прервана из-за недостатка игроков.");
                        session.State = GameState.GameOver; 
                    }
                }

                else 
                {
                    if (session.Players.Count == 0)
                    {
                        _sessionManager.RemoveSession(session.Id);
                        Console.WriteLine($"Игра {session.Id} удалена, так как создатель отключился и игроков больше нет.");
                    }
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
}