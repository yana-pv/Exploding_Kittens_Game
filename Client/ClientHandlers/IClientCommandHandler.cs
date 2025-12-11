namespace Client;

public interface IClientCommandHandler
{
    Task Handle(GameClient client, byte[] payload);
}