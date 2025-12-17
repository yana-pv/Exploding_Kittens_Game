using Server.Networking.Commands;
using System.Reflection;

namespace Client.ClientHandlers;

public class ClientCommandHandlerFactory
{
    private static readonly Lazy<Dictionary<Command, IClientCommandHandler>> Handlers =
        new(BuildHandlers);

    private static Dictionary<Command, IClientCommandHandler> BuildHandlers()
    {
        var map = new Dictionary<Command, IClientCommandHandler>();

        var types = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        typeof(IClientCommandHandler).IsAssignableFrom(t));

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<ClientCommandAttribute>();
            if (attr == null) continue;

            var handler = (IClientCommandHandler)Activator.CreateInstance(type)!;
            map[attr.Command] = handler;
        }

        return map;
    }

    public IClientCommandHandler GetHandler(Command cmd)
    {
        return Handlers.Value.TryGetValue(cmd, out var handler)
            ? handler
            : throw new KeyNotFoundException($"Client handler not found for command {cmd}");
    }
}