using Server.Game.Enums;
using Server.Networking.Commands;

namespace Client.ClientHandlers;

[ClientCommand(Command.Error)]
public class ErrorHandler : IClientCommandHandler
{
    public Task Handle(GameClient client, byte[] payload)
    {
        if (payload.Length > 0)
        {
            var error = (CommandResponse)payload[0];
            client.AddToLog($"❌ Ошибка: {GetErrorMessage(error)}");
        }

        return Task.CompletedTask;
    }

    private string GetErrorMessage(CommandResponse error)
    {
        return error switch
        {
            CommandResponse.GameNotFound => "Игра не найдена",
            CommandResponse.PlayerNotFound => "Игрок не найден",
            CommandResponse.NotYourTurn => "Не ваш ход",
            CommandResponse.InvalidAction => "Недопустимое действие",
            CommandResponse.GameFull => "Игра заполнена",
            CommandResponse.GameAlreadyStarted => "Игра уже началась",
            CommandResponse.CardNotFound => "Карта не найдена",
            CommandResponse.NotEnoughCards => "Недостаточно карт",
            CommandResponse.PlayerNotAlive => "Игрок выбыл",
            CommandResponse.SessionNotFound => "Сессия не найдена",
            CommandResponse.Unauthorized => "Неавторизованный доступ",
            _ => $"Неизвестная ошибка: {error}"
        };
    }
}