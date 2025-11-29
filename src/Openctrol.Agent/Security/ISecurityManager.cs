namespace Openctrol.Agent.Security;

public interface ISecurityManager
{
    bool IsHaAllowed(string haId);
    SessionToken IssueDesktopSessionToken(string haId, TimeSpan ttl);
    bool TryValidateDesktopSessionToken(string token, out SessionToken validated);
}

