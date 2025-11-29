namespace Openctrol.Agent.Config;

public interface IConfigManager
{
    AgentConfig GetConfig();
    void Reload();
}

