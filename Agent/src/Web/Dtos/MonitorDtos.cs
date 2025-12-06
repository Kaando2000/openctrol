namespace Openctrol.Agent.Web.Dtos;

public sealed class MonitorInfoDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Resolution { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class MonitorsResponse
{
    public IReadOnlyList<MonitorInfoDto> Monitors { get; init; } = Array.Empty<MonitorInfoDto>();
    public string CurrentMonitorId { get; init; } = "";
}

public sealed class SelectMonitorRequest
{
    public string MonitorId { get; init; } = "";
}

public sealed class SelectMonitorResponse
{
    public string Status { get; init; } = "ok";
    public string MonitorId { get; init; } = "";
}

