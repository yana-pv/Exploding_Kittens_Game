using exploding_kittens.ClientModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace exploding_kittens.Networking
{
    public class NetworkClient
    {
        private Socket _socket;
        private bool _isConnected = false;
        private Guid _playerId;
        private Guid _gameId;

        public event Action<string> OnMessageReceived;
        public event Action<List<ClientCardDto>> OnHandUpdated;
        public event Action<GameStateDto> OnGameStateUpdated;
        public event Action<string> OnErrorReceived;
        public event Action<Guid, Guid> OnGameJoined;

        public bool IsConnected => _isConnected && _socket != null && _socket.Connected == true;
        public Guid PlayerId => _playerId;
        public Guid GameId => _gameId;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await Task.Factory.FromAsync(
                    _socket.BeginConnect(ipAddress, port, null, null),
                    _socket.EndConnect);
                _isConnected = true;

                // Запускаем прослушивание сообщений
                Task.Run((Func<Task>)ReceiveMessages);

                return true;
            }
            catch (Exception ex)
            {
                OnErrorReceived?.Invoke($"Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        private async Task ReceiveMessages()
        {
            byte[] buffer = new byte[4096];

            while (_isConnected && _socket != null && _socket.Connected == true)
            {
                try
                {
                    // Используем асинхронный Receive для .NET Framework
                    var receiveTask = Task<int>.Factory.FromAsync(
                        _socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, null, null),
                        _socket.EndReceive);

                    var bytesRead = await receiveTask;

                    if (bytesRead == 0)
                    {
                        Disconnect();
                        break;
                    }

                    var data = new byte[bytesRead];
                    Array.Copy(buffer, 0, data, 0, bytesRead);

                    ProcessReceivedData(data);
                }
                catch
                {
                    Disconnect();
                    break;
                }
            }
        }

        private void ProcessReceivedData(byte[] data)
        {
            try
            {
                // Анализируем пакет (аналогично серверному протоколу)
                if (data.Length < 5) return;

                // START_BYTE + COMMAND + LENGTH(2) + PAYLOAD + END_BYTE
                if (data[0] != 0x02 || data[data.Length - 1] != 0x03) return;

                var command = (Command)data[1];
                ushort length = (ushort)(data[2] | (data[3] << 8));

                if (length > 0 && data.Length >= 5 + length)
                {
                    var payload = new byte[length];
                    Array.Copy(data, 4, payload, 0, length);
                    ProcessCommand(command, payload);
                }
                else
                {
                    ProcessCommand(command, new byte[0]);
                }
            }
            catch (Exception ex)
            {
                OnErrorReceived?.Invoke($"Ошибка обработки данных: {ex.Message}");
            }
        }

        private void ProcessCommand(Command command, byte[] payload)
        {
            switch (command)
            {
                case Command.Message:
                    var message = Encoding.UTF8.GetString(payload);
                    OnMessageReceived?.Invoke(message);
                    break;

                case Command.GameCreated:
                    var gameData = Encoding.UTF8.GetString(payload);
                    var parts = gameData.Split(':');
                    if (parts.Length == 2 &&
                        Guid.TryParse(parts[0], out var gameId) &&
                        Guid.TryParse(parts[1], out var playerId))
                    {
                        _gameId = gameId;
                        _playerId = playerId;
                        OnGameJoined?.Invoke(gameId, playerId);
                    }
                    break;

                case Command.PlayerHandUpdate:
                    var handJson = Encoding.UTF8.GetString(payload);
                    var hand = PlayerHandDto.FromJson(handJson);
                    if (hand != null)
                    {
                        OnHandUpdated?.Invoke(hand.Cards);
                    }
                    break;

                case Command.GameStateUpdate:
                    var stateJson = Encoding.UTF8.GetString(payload);
                    try
                    {
                        var state = JsonConvert.DeserializeObject<GameStateDto>(stateJson);
                        if (state != null)
                        {
                            OnGameStateUpdated?.Invoke(state);
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки десериализации
                    }
                    break;

                case Command.Error:
                    if (payload.Length > 0)
                    {
                        var errorCode = (CommandResponse)payload[0];
                        OnErrorReceived?.Invoke($"Ошибка: {errorCode}");
                    }
                    break;
            }
        }

        public async Task SendCommandAsync(Command command, string payload = "")
        {
            if (!IsConnected) return;

            try
            {
                var payloadBytes = Encoding.UTF8.GetBytes(payload);
                var packet = BuildPacket(command, payloadBytes);

                await Task.Factory.FromAsync(
                    _socket.BeginSend(packet, 0, packet.Length, SocketFlags.None, null, null),
                    _socket.EndSend);
            }
            catch (Exception ex)
            {
                OnErrorReceived?.Invoke($"Ошибка отправки: {ex.Message}");
            }
        }

        private byte[] BuildPacket(Command command, byte[] payload)
        {
            var packet = new byte[1 + 1 + 2 + payload.Length + 1];

            packet[0] = 0x02; // START_BYTE
            packet[1] = (byte)command;

            // Длина payload (ushort, little endian)
            ushort length = (ushort)payload.Length;
            packet[2] = (byte)(length & 0xFF);
            packet[3] = (byte)((length >> 8) & 0xFF);

            if (payload.Length > 0)
            {
                Array.Copy(payload, 0, packet, 4, payload.Length);
            }

            packet[packet.Length - 1] = 0x03; // END_BYTE

            return packet;
        }

        public void Disconnect()
        {
            _isConnected = false;
            try
            {
                _socket?.Close();
            }
            catch
            {
                // Игнорируем ошибки при закрытии
            }
        }

        // Методы для отправки команд
        public Task CreateGameAsync(string playerName)
        {
            return SendCommandAsync(Command.CreateGame, playerName);
        }

        public Task JoinGameAsync(Guid gameId, string playerName)
        {
            return SendCommandAsync(Command.JoinGame, $"{gameId}:{playerName}");
        }

        public Task StartGameAsync(Guid gameId)
        {
            return SendCommandAsync(Command.StartGame, gameId.ToString());
        }

        public Task PlayCardAsync(Guid gameId, Guid playerId, int cardIndex, string additionalData = null)
        {
            var payload = additionalData != null
                ? $"{gameId}:{playerId}:{cardIndex}:{additionalData}"
                : $"{gameId}:{playerId}:{cardIndex}";
            return SendCommandAsync(Command.PlayCard, payload);
        }

        public Task DrawCardAsync(Guid gameId, Guid playerId)
        {
            return SendCommandAsync(Command.DrawCard, $"{gameId}:{playerId}");
        }

        public Task EndTurnAsync(Guid gameId, Guid playerId)
        {
            return SendCommandAsync(Command.EndTurn, $"{gameId}:{playerId}");
        }

        public Task UseComboAsync(Guid gameId, Guid playerId, int comboType, string cardIndices, string targetData = null)
        {
            var payload = targetData != null
                ? $"{gameId}:{playerId}:{comboType}:{cardIndices}:{targetData}"
                : $"{gameId}:{playerId}:{comboType}:{cardIndices}";
            return SendCommandAsync(Command.UseCombo, payload);
        }

        public Task PlayNopeAsync(Guid gameId, Guid playerId)
        {
            return SendCommandAsync(Command.PlayNope, $"{gameId}:{playerId}");
        }

        public Task PlayDefuseAsync(Guid gameId, Guid playerId, int position)
        {
            return SendCommandAsync(Command.PlayDefuse, $"{gameId}:{playerId}:{position}");
        }

        public Task PlayFavorAsync(Guid gameId, Guid playerId, int cardIndex)
        {
            return SendCommandAsync(Command.PlayFavor, $"{gameId}:{playerId}:{cardIndex}");
        }

        public Task StealCardAsync(Guid gameId, Guid playerId, int cardIndex)
        {
            return SendCommandAsync(Command.StealCard, $"{gameId}:{playerId}:{cardIndex}");
        }

        public Task TakeFromDiscardAsync(Guid gameId, Guid playerId, int cardIndex)
        {
            return SendCommandAsync(Command.TakeFromDiscard, $"{gameId}:{playerId}:{cardIndex}");
        }

        public Task GetGameStateAsync(Guid gameId)
        {
            return SendCommandAsync(Command.GetGameState, gameId.ToString());
        }
    }

    public enum Command : byte
    {
        CreateGame = 0x10,
        JoinGame = 0x11,
        StartGame = 0x13,
        EndTurn = 0x14,
        PlayCard = 0x20,
        DrawCard = 0x21,
        UseCombo = 0x22,
        TargetPlayer = 0x23,
        PlayNope = 0x24,
        PlayDefuse = 0x25,
        PlayFavor = 0x26,
        StealCard = 0x27,
        TakeFromDiscard = 0x28,
        GetGameState = 0x30,
        GameCreated = 0x40,
        PlayerHandUpdate = 0x44,
        GameStateUpdate = 0x43,
        Error = 0x49,
        Message = 0x4A
    }

    public enum CommandResponse : byte
    {
        Ok,
        GameNotFound,
        PlayerNotFound,
        NotYourTurn,
        InvalidAction,
        GameFull,
        GameAlreadyStarted,
        CardNotFound,
        NotEnoughCards,
        PlayerNotAlive,
        SessionNotFound,
        Unauthorized
    }
}