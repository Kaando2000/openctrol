namespace Openctrol.Agent.SystemState;

public interface ISystemStateMonitor
{
    SystemStateSnapshot GetCurrent();
    event EventHandler<SystemStateSnapshot>? StateChanged;
}

