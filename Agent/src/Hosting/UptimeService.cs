namespace Openctrol.Agent.Hosting;

/// <summary>
/// Independent implementation of IUptimeService that tracks process start time.
/// Has no dependencies on IControlApiServer or AgentHost.
/// </summary>
public sealed class UptimeService : IUptimeService
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public long GetUptimeSeconds() => (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds;

    public DateTimeOffset GetStartTime() => _startTime;
}

