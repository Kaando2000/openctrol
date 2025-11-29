using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Web;

public sealed class SessionBroker : ISessionBroker, IDisposable
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DesktopSession> _sessions = new();
    private readonly Dictionary<string, CancellationTokenSource> _webSocketHandlers = new(); // Track active WebSocket handlers
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly object _lock = new();

    public SessionBroker(IConfigManager configManager, ILogger logger)
    {
        _configManager = configManager;
        _logger = logger;

        // Clean up expired sessions periodically - store timer to prevent GC
        _cleanupTimer = new System.Threading.Timer(CleanupExpiredSessions, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public DesktopSession StartDesktopSession(string haId, TimeSpan ttl)
    {
        var config = _configManager.GetConfig();

        lock (_lock)
        {
            // Check MaxSessions limit
            var activeSessions = _sessions.Values.Count(s => s.IsActive && s.ExpiresAt > DateTimeOffset.UtcNow);
            if (activeSessions >= config.MaxSessions)
            {
                throw new InvalidOperationException($"Maximum sessions limit ({config.MaxSessions}) reached");
            }

            var sessionId = Guid.NewGuid().ToString();
            var session = new DesktopSession
            {
                SessionId = sessionId,
                HaId = haId,
                ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
                StartedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };

            _sessions[sessionId] = session;
            _logger.Info($"[Session] Started desktop session {sessionId} for HA ID: {haId}");
            return session;
        }
    }

    public bool TryGetSession(string sessionId, out DesktopSession session)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var s) && s.ExpiresAt > DateTimeOffset.UtcNow)
            {
                session = s;
                return true;
            }
        }

        session = null!;
        return false;
    }

    public void EndSession(string sessionId)
    {
        CancellationTokenSource? handlerCancellation = null;
        
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.IsActive = false;
                _sessions.Remove(sessionId);
            }
            
            // Signal any active WebSocket handler to close immediately
            if (_webSocketHandlers.TryGetValue(sessionId, out handlerCancellation))
            {
                _webSocketHandlers.Remove(sessionId);
            }
        }
        
        // Cancel outside lock to avoid deadlock
        if (handlerCancellation != null)
        {
            try
            {
                handlerCancellation.Cancel();
                _logger.Info($"[Session] Ended desktop session {sessionId} and signaled WebSocket handler to close");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Session] Error signaling WebSocket handler for session {sessionId}", ex);
            }
        }
        else
        {
            _logger.Info($"[Session] Ended desktop session {sessionId}");
        }
    }
    
    public void RegisterWebSocketHandler(string sessionId, CancellationTokenSource cancellationTokenSource)
    {
        lock (_lock)
        {
            _webSocketHandlers[sessionId] = cancellationTokenSource;
        }
    }
    
    public void UnregisterWebSocketHandler(string sessionId)
    {
        lock (_lock)
        {
            _webSocketHandlers.Remove(sessionId);
        }
    }

    public IReadOnlyList<DesktopSession> GetActiveSessions()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            return _sessions.Values
                .Where(s => s.IsActive && s.ExpiresAt > now)
                .ToList();
        }
    }

    private void CleanupExpiredSessions(object? state)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var expiredSessions = _sessions
                .Where(kvp => kvp.Value.ExpiresAt <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in expiredSessions)
            {
                _sessions.Remove(sessionId);
            }

            if (expiredSessions.Count > 0)
            {
                _logger.Info($"[Session] Cleaned up {expiredSessions.Count} expired sessions");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

