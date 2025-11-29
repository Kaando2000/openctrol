namespace Openctrol.Agent.Hosting;

public interface IUptimeService
{
    long GetUptimeSeconds();
    DateTimeOffset GetStartTime();
}

