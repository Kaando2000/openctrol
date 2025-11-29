namespace Openctrol.Agent.RemoteDesktop;

public sealed class RemoteDesktopStatus
{
    public bool IsRunning { get; init; }
    public DateTimeOffset LastFrameAt { get; init; }
    public string State { get; init; } = "unknown";
}

