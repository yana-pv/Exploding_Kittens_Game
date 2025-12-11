using System.Reflection;

namespace Server.Networking.Commands;

public static class CommandHandlerFactory
{
    private static readonly Lazy<Dictionary<Command, ICommandHandler>> CommandHandlers = new(BuildAllHandlers);

    private static Dictionary<Command, ICommandHandler> BuildAllHandlers()
    {
        var allHandlers = new Dictionary<Command, ICommandHandler>();
        var handlerTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(ICommandHandler).IsAssignableFrom(type));

        foreach (var handlerType in handlerTypes)
        {
            var attribute = handlerType.GetCustomAttribute<CommandAttribute>();
            if (attribute == null) continue;

            var handler = (ICommandHandler)Activator.CreateInstance(handlerType)!;
            allHandlers.Add(attribute.Command, handler);
        }

        return allHandlers;
    }

    public static ICommandHandler GetHandler(Command command)
    {
        return CommandHandlers.Value.TryGetValue(command, out var handler)
            ? handler
            : throw new NotSupportedException($"No handler found for command {command}");
    }
}