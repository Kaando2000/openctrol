namespace Openctrol.Agent.RemoteDesktop;

public interface IFrameSubscriber
{
    void OnFrame(RemoteFrame frame);
}

