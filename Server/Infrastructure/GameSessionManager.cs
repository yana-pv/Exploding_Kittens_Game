using Server.Game.Models;
using Shared.Models;
using System.Collections.Concurrent;

namespace Server.Infrastructure;

public class GameSessionManager
{
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();
    private readonly Timer _cleanupTimer;

    public GameSessionManager()
    {
        // Очистка неактивных сессий каждые 5 минут
        _cleanupTimer = new Timer(CleanupInactiveSessions, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public GameSession? GetSession(Guid sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public bool CreateSession(GameSession session)
    {
        return _sessions.TryAdd(session.Id, session);
    }

    public bool RemoveSession(Guid sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }

    public IEnumerable<GameSession> GetActiveSessions()
    {
        return _sessions.Values.Where(s =>
            s.State != GameState.GameOver &&
            s.Players.Count > 0 &&
            DateTime.UtcNow - s.CreatedAt < TimeSpan.FromHours(1));
    }

    private void CleanupInactiveSessions(object? state)
    {
        var inactiveSessions = _sessions.Values
            .Where(s => s.State == GameState.GameOver ||
                       s.Players.Count == 0 ||
                       DateTime.UtcNow - s.CreatedAt > TimeSpan.FromHours(1))
            .ToList();

        foreach (var session in inactiveSessions)
        {
            _sessions.TryRemove(session.Id, out _);
        }
    }

    public IEnumerable<GameSession> GetWaitingGames()
    {
        return _sessions.Values
            .Where(s => s.State == GameState.WaitingForPlayers &&
                       s.Players.Count < s.MaxPlayers &&
                       DateTime.UtcNow - s.CreatedAt < TimeSpan.FromHours(1))
            .OrderByDescending(s => s.CreatedAt);
    }

    public int GetWaitingGamesCount()
    {
        return GetWaitingGames().Count();
    }
}