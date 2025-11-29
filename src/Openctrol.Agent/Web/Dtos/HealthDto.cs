namespace Openctrol.Agent.Web.Dtos;

public sealed class HealthDto
{
    public string AgentId { get; init; } = "";
    public long UptimeSeconds { get; init; }
    public RemoteDesktopHealthDto RemoteDesktop { get; init; } = new();
    public int ActiveSessions { get; init; }
}

public sealed class RemoteDesktopHealthDto
{
    public bool IsRunning { get; init; }
    public DateTimeOffset LastFrameAt { get; init; }
    public string State { get; init; } = "unknown";
}

