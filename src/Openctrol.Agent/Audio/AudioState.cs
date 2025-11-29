namespace Openctrol.Agent.Audio;

public sealed class AudioState
{
    public string DefaultOutputDeviceId { get; init; } = "";
    public IReadOnlyList<AudioDeviceInfo> Devices { get; init; } = Array.Empty<AudioDeviceInfo>();
    public IReadOnlyList<AudioSessionInfo> Sessions { get; init; } = Array.Empty<AudioSessionInfo>();
}

