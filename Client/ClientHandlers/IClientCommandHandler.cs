namespace Client.ClientHandlers;

public interface IClientCommandHandler
{
    Task Handle(GameClient client, byte[] payload);
}