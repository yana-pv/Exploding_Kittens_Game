using Server.Infrastructure;
using System.Net.Sockets;

namespace Server.Networking.Commands;

public interface ICommandHandler
{
    Task Invoke(Socket sender, GameSessionManager sessionManager, byte[]? payload = null, CancellationToken ct = default);
}