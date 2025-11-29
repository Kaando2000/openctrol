namespace Openctrol.Agent.Web;

public interface IControlApiServer
{
    void Start();
    Task StopAsync();
}

