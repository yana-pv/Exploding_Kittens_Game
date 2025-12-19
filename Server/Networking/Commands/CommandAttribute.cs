using Shared.Models;

namespace Server.Networking.Commands;

[AttributeUsage(AttributeTargets.Class)]
public class CommandAttribute(Command command) : Attribute
{
    public Command Command { get; } = command;
}