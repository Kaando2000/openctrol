namespace Openctrol.Agent.Web.Dtos;

public sealed class AudioStateResponse
{
    public string DefaultOutputDeviceId { get; init; } = "";
    public IReadOnlyList<AudioDeviceInfoDto> Devices { get; init; } = Array.Empty<AudioDeviceInfoDto>();
    public IReadOnlyList<AudioSessionInfoDto> Sessions { get; init; } = Array.Empty<AudioSessionInfoDto>();
}

public sealed class AudioDeviceInfoDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class AudioSessionInfoDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
}

public sealed class SetDeviceVolumeRequest
{
    public string DeviceId { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
}

public sealed class SetSessionVolumeRequest
{
    public string SessionId { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
}

public sealed class SetDefaultDeviceRequest
{
    public string DeviceId { get; init; } = "";
}

