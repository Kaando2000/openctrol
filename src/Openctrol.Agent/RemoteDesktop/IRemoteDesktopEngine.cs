using Openctrol.Agent.Input;

namespace Openctrol.Agent.RemoteDesktop;

public interface IRemoteDesktopEngine
{
    void Start();
    void Stop();

    RemoteDesktopStatus GetStatus();
    IReadOnlyList<MonitorInfo> GetMonitors();
    void SelectMonitor(string monitorId);
    string GetCurrentMonitorId();

    void RegisterFrameSubscriber(IFrameSubscriber subscriber);
    void UnregisterFrameSubscriber(IFrameSubscriber subscriber);

    void InjectPointer(PointerEvent evt);
    void InjectKey(KeyboardEvent evt);
}

