namespace Openctrol.Agent.Web;

public sealed class DesktopSession
{
    public string SessionId { get; init; } = "";
    public string HaId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public bool IsActive { get; set; }
}

