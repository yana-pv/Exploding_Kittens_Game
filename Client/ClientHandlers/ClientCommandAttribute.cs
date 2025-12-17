using Server.Networking.Commands;

namespace Client.ClientHandlers;

[AttributeUsage(AttributeTargets.Class)]
public class ClientCommandAttribute(Command command) : Attribute
{
    public Command Command { get; } = command;
}