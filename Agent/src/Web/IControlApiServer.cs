namespace Openctrol.Agent.Web;

public interface IControlApiServer
{
    void Start();
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}

