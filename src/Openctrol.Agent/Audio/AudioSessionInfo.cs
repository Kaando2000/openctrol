namespace Openctrol.Agent.Audio;

public sealed class AudioSessionInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
}

