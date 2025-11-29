namespace Openctrol.Agent.Audio;

public interface IAudioManager
{
    AudioState GetState();
    void SetDeviceVolume(string deviceId, float volume, bool muted);
    void SetSessionVolume(string sessionId, float volume, bool muted);
    void SetDefaultOutputDevice(string deviceId);
}

