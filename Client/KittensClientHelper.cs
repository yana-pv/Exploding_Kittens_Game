using Server.Networking.Commands;
using Server.Networking.Protocol;
using System.Net.Sockets;
using System.Text;

namespace Client;

public class KittensClientHelper(Socket socket)
{
    public Task SendCreateGame(string playerName)
    {
        var payload = Encoding.UTF8.GetBytes(playerName);
        Console.WriteLine($"SendCreateGame payload: '{playerName}', length: {payload.Length}");
        var package = new KittensPackageBuilder(payload, Command.CreateGame);
        var packageBytes = package.Build();
        Console.WriteLine($"Package length: {packageBytes.Length}");
        Console.WriteLine($"Package bytes: {BitConverter.ToString(packageBytes)}");
        return socket.SendAsync(packageBytes, SocketFlags.None);
    }

    public Task SendJoinGame(Guid gameId, string playerName)
    {
        var payloadString = $"{gameId}:{playerName}";
        var payload = Encoding.UTF8.GetBytes(payloadString);
        Console.WriteLine($"SendJoinGame PAYLOAD STRING: '{payloadString}', length: {payloadString.Length}");
        Console.WriteLine($"SendJoinGame PAYLOAD BYTES: {BitConverter.ToString(payload)}"); // <-- Добавить
        var package = new KittensPackageBuilder(payload, Command.JoinGame);
        var packageBytes = package.Build(); // <-- Добавить
        Console.WriteLine($"SendJoinGame PACKAGE BYTES: {BitConverter.ToString(packageBytes)}"); // <-- Добавить
        return socket.SendAsync(packageBytes, SocketFlags.None);
    }

    public Task SendStartGame(Guid gameId)
    {
        var payload = Encoding.UTF8.GetBytes(gameId.ToString());
        var package = new KittensPackageBuilder(payload, Command.StartGame);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendPlayCard(Guid gameId, Guid playerId, int cardIndex, string? targetPlayerId = null)
    {
        var payload = targetPlayerId != null
            ? Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{cardIndex}:{targetPlayerId}")
            : Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{cardIndex}");

        var package = new KittensPackageBuilder(payload, Command.PlayCard);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendDrawCard(Guid gameId, Guid playerId)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}");
        var package = new KittensPackageBuilder(payload, Command.DrawCard);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendUseCombo(Guid gameId, Guid playerId, int comboType, List<int> cardIndices, string? targetData = null)
    {
        var indicesStr = string.Join(",", cardIndices);
        var payload = targetData != null
            ? Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{comboType}:{indicesStr}:{targetData}")
            : Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{comboType}:{indicesStr}");

        Console.WriteLine($"DEBUG SendUseCombo: {gameId}:{playerId}:{comboType}:{indicesStr}:{targetData}");

        var package = new KittensPackageBuilder(payload, Command.UseCombo);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendPlayNope(Guid gameId, Guid playerId, Guid actionId)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{actionId}");
        var package = new KittensPackageBuilder(payload, Command.PlayNope);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendPlayDefuse(Guid gameId, Guid playerId, int position)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{position}");
        var package = new KittensPackageBuilder(payload, Command.PlayDefuse);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendGetGameState(Guid gameId)
    {
        var payload = Encoding.UTF8.GetBytes(gameId.ToString());
        var package = new KittensPackageBuilder(payload, Command.GetGameState);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendGetPlayerHand(Guid gameId, Guid playerId)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}");
        var package = new KittensPackageBuilder(payload, Command.GetPlayerHand);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendEndTurn(Guid gameId, Guid playerId)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}");
        var package = new KittensPackageBuilder(payload, Command.EndTurn);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendChooseCard(Guid gameId, Guid playerId, int cardIndex)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{cardIndex}");
        var package = new KittensPackageBuilder(payload, Command.TargetPlayer); // Используем TargetPlayer
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendFavorResponse(Guid gameId, Guid playerId, int cardIndex)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{cardIndex}");
        Console.WriteLine($"DEBUG Client: Отправляем FavorResponse: {gameId}:{playerId}:{cardIndex}");
        var package = new KittensPackageBuilder(payload, Command.PlayFavor);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendStealCard(Guid gameId, Guid playerId, int cardIndex)
    {
        // Отправляем только ID игры, ID игрока и номер карты
        // Сервер сам найдет цель из PendingStealAction
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{cardIndex}");
        var package = new KittensPackageBuilder(payload, Command.StealCard);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }

    public Task SendTakeFromDiscard(Guid gameId, Guid playerId, int cardIndex)
    {
        var payload = Encoding.UTF8.GetBytes($"{gameId}:{playerId}:{cardIndex}");
        var package = new KittensPackageBuilder(payload, Command.TakeFromDiscard);
        return socket.SendAsync(package.Build(), SocketFlags.None);
    }
}