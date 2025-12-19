using Server.Game.Models;
using Shared.Models;
using Shared.Protocol;
using System.Net.Sockets;


namespace Server.Networking;

public static class SocketExtensions
{
    public static async Task SendError(this Socket socket, CommandResponse error)
    {
        await socket.SendAsync(KittensPackageBuilder.ErrorResponse(error), SocketFlags.None);
    }

    public static async Task SendMessage(this Socket socket, string message)
    {
        await socket.SendAsync(KittensPackageBuilder.MessageResponse(message), SocketFlags.None);
    }

    public static async Task SendPlayerHand(this Socket socket, Player player)
    {
        await socket.SendAsync(KittensPackageBuilder.PlayerHandResponse(player.Hand),
            SocketFlags.None);
    }

    public static async Task BroadcastToAll(this GameSession session, byte[] data)
    {
        var tasks = session.Players
            .Where(p => p.IsAlive || session.State == GameState.WaitingForPlayers)
            .Select(p => p.Connection.SendAsync(data, SocketFlags.None));

        await Task.WhenAll(tasks);
    }

    public static async Task BroadcastGameState(this GameSession session)
    {
        var gameStateData = KittensPackageBuilder.GameStateResponse(session.GetGameStateJson());
        await session.BroadcastToAll(gameStateData);
    }

    public static async Task BroadcastMessage(this GameSession session, string message)
    {
        var messageData = KittensPackageBuilder.MessageResponse(message);
        await session.BroadcastToAll(messageData);
    }
}