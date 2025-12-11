using Server.Game.Models;
using Server.Infrastructure;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Server.Networking.Commands;

public interface ICommandHandler
{
    // Уберите ConcurrentDictionary<Guid, GameSession> из параметров Invoke
    // Task Invoke(Socket sender, ConcurrentDictionary<Guid, GameSession> gameSessions, ...);
    // Сделайте так:
    Task Invoke(Socket sender, GameSessionManager sessionManager, byte[]? payload = null, CancellationToken ct = default);
}