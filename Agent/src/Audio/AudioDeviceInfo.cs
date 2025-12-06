namespace Openctrol.Agent.Audio;

public sealed class AudioDeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
    public bool IsDefault { get; init; }
}

