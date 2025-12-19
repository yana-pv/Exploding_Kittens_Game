using Server.Game.Models;
using Server.Infrastructure;
using Shared.Models;
using Shared.Protocol;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Networking.Commands.Handlers;

[Command(Command.GetAvailableGames)]
public class GetAvailableGamesHandler : ICommandHandler
{
    public async Task Invoke(Socket sender, GameSessionManager sessionManager,
        byte[]? payload = null, CancellationToken ct = default)
    {
        try
        {
            var activeSessions = sessionManager.GetActiveSessions()
                .Where(s => s.State == GameState.WaitingForPlayers && s.Players.Count < s.MaxPlayers)
                .ToList();

            var gameInfos = activeSessions.Select(s => new GameSessionInfoDto
            {
                Id = s.Id,
                CreatorName = s.Players.FirstOrDefault()?.Name ?? "Неизвестно",
                PlayersCount = s.Players.Count,
                MaxPlayers = s.MaxPlayers,
                State = s.State,
                CreatedAt = s.CreatedAt
            }).ToList();

            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            };

            var json = JsonSerializer.Serialize(gameInfos, options);
            var bytes = Encoding.UTF8.GetBytes(json);

            var package = new KittensPackageBuilder(bytes, Command.GameList);
            await sender.SendAsync(package.Build(), SocketFlags.None);
        }
        catch (Exception ex)
        {
            await sender.SendMessage($"Ошибка получения списка игр: {ex.Message}");
        }
    }
}