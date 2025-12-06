namespace Openctrol.Agent.SystemState;

public sealed class SystemStateSnapshot
{
    public int ActiveSessionId { get; init; }
    public DesktopState DesktopState { get; init; }
}

