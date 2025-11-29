namespace Openctrol.Agent.Web;

public interface ISessionBroker
{
    DesktopSession StartDesktopSession(string haId, TimeSpan ttl);
    bool TryGetSession(string sessionId, out DesktopSession session);
    void EndSession(string sessionId);
    IReadOnlyList<DesktopSession> GetActiveSessions();
}

