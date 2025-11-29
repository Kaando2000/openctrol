namespace Openctrol.Agent.RemoteDesktop;

public sealed class MonitorInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
}

