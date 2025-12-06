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
    public string OutputDeviceId { get; init; } = ""; // Device ID this session is routed to
}

public sealed class SetDeviceVolumeRequest
{
    public string DeviceId { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
    public bool SetDefault { get; init; } = false; // If true, also set as default output device
}

public sealed class SetSessionVolumeRequest
{
    public string SessionId { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
    public string? OutputDeviceId { get; init; } = null; // Optional: route session to specific device
}

public sealed class SetSessionDeviceRequest
{
    public string SessionId { get; init; } = "";
    public string DeviceId { get; init; } = "";
}

public sealed class AudioStatusResponse
{
    public AudioMasterDto Master { get; init; } = new();
    public IReadOnlyList<AudioDeviceInfoDto> Devices { get; init; } = Array.Empty<AudioDeviceInfoDto>();
}

public sealed class AudioMasterDto
{
    public float Volume { get; init; } // 0-100
    public bool Muted { get; init; }
}

public sealed class SetMasterVolumeRequest
{
    public float? Volume { get; init; } = null; // 0-100, optional
    public bool? Muted { get; init; } = null; // Optional
}

public sealed class SetDeviceVolumeSimpleRequest
{
    public string DeviceId { get; init; } = "";
    public float? Volume { get; init; } = null; // 0-100, optional
    public bool? Muted { get; init; } = null; // Optional
}

public sealed class SetDefaultDeviceRequest
{
    public string DeviceId { get; init; } = "";
}

public sealed class StatusResponse
{
    public string Status { get; init; } = "ok";
}

public sealed class ErrorResponse
{
    public string Error { get; init; } = "";
    public string Details { get; init; } = "";
}

