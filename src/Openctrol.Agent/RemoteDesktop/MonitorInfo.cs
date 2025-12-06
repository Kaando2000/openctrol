namespace Openctrol.Agent.RemoteDesktop;

public sealed class MonitorInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int X { get; init; }  // Monitor position X in virtual desktop coordinates
    public int Y { get; init; }  // Monitor position Y in virtual desktop coordinates
    public bool IsPrimary { get; init; }
}

