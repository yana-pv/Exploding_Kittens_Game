using Client.ClientHandlers;
using Shared.Models;
using Shared.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Client.Networking;

public class GameMessageListener
{
    private readonly GameClient _client;
    private readonly Socket _socket;
    private readonly ClientCommandHandlerFactory _handlerFactory;
    private readonly List<byte> _receiveBuffer;

    public GameMessageListener(
        GameClient client,
        Socket socket,
        ClientCommandHandlerFactory handlerFactory,
        List<byte> receiveBuffer)
    {
        _client = client;
        _socket = socket;
        _handlerFactory = handlerFactory;
        _receiveBuffer = receiveBuffer;
    }

    public async Task ListenForServerMessages(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (_client.Running && !cancellationToken.IsCancellationRequested && _socket.Connected)
            {
                var bytesRead = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                if (bytesRead == 0) break;

                var data = new byte[bytesRead];
                Array.Copy(buffer, 0, data, 0, bytesRead);

                await ProcessServerMessage(data);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        {
            _client.PrintError("Соединение с сервером разорвано.");
            _client.Running = false;
        }
        catch (Exception ex)
        {
            _client.PrintError($"Ошибка приема данных: {ex.Message}");
            _client.Running = false;
        }
    }

    private async Task ProcessServerMessage(byte[] data)
    {
        _receiveBuffer.AddRange(data);

        while (_receiveBuffer.Count >= 5)
        {
            int startIndex = -1;
            for (int i = 0; i <= _receiveBuffer.Count - 5; i++)
            {
                if (_receiveBuffer[i] == 0x02)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == -1)
            {
                _receiveBuffer.Clear();
                break;
            }

            if (startIndex > 0)
            {
                _receiveBuffer.RemoveRange(0, startIndex);
                continue;
            }

            var command = _receiveBuffer[1];
            ushort payloadLength = (ushort)(_receiveBuffer[2] | (_receiveBuffer[3] << 8));
            var expectedTotalLength = 1 + 1 + KittensPackageMeta.LengthSize + payloadLength + 1;

            if (_receiveBuffer.Count >= expectedTotalLength)
            {
                var endIndex = expectedTotalLength - 1;
                if (endIndex >= _receiveBuffer.Count || _receiveBuffer[endIndex] != 0x03)
                {
                    _receiveBuffer.RemoveAt(0);
                    continue;
                }

                var packet = _receiveBuffer.Take(expectedTotalLength).ToArray();
                _receiveBuffer.RemoveRange(0, expectedTotalLength);

                var parsed = KittensPackageParser.TryParse(packet, out var error);
                if (parsed != null)
                {
                    var (cmd, payload) = parsed.Value;
                    try
                    {
                        var handler = _handlerFactory.GetHandler(cmd);
                        await handler.Handle(_client, payload);
                    }
                    catch (KeyNotFoundException)
                    {
                        await HandleCommandFallback(cmd, payload);
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    private async Task HandleCommandFallback(Command command, byte[] payload)
    {
        switch (command)
        {
            case Command.Message:
                var message = Encoding.UTF8.GetString(payload);
                _client.AddToLog(message);
                break;

            case Command.Error:
                if (payload.Length > 0)
                {
                    var error = (CommandResponse)payload[0];
                    _client.AddToLog($"❌ Ошибка: {GetErrorMessage(error)}");
                }
                break;

            default:
                break;
        }

        await Task.CompletedTask;
    }

    private string GetErrorMessage(CommandResponse error)
    {
        return error switch
        {
            CommandResponse.GameNotFound => "Игра не найдена",
            CommandResponse.PlayerNotFound => "Игрок не найден",
            CommandResponse.NotYourTurn => "Не ваш ход",
            CommandResponse.InvalidAction => "Недопустимое действие",
            CommandResponse.GameFull => "Игра заполнена",
            CommandResponse.GameAlreadyStarted => "Игра уже началась",
            CommandResponse.CardNotFound => "Карта не найдена",
            CommandResponse.NotEnoughCards => "Недостаточно карт",
            CommandResponse.PlayerNotAlive => "Игрок выбыл",
            CommandResponse.SessionNotFound => "Сессия не найдена",
            CommandResponse.Unauthorized => "Неавторизованный доступ",
            _ => $"Ошибка: {error}"
        };
    }
}