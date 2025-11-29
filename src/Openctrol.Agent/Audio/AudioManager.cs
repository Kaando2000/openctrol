using NAudio.CoreAudioApi;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Audio;

public sealed class AudioManager : IAudioManager
{
    private readonly ILogger _logger;
    private readonly MMDeviceEnumerator _deviceEnumerator;

    public AudioManager(ILogger logger)
    {
        _logger = logger;
        _deviceEnumerator = new MMDeviceEnumerator();
    }

    public AudioState GetState()
    {
        try
        {
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var defaultDeviceId = defaultDevice?.ID ?? "";

            var devices = new List<AudioDeviceInfo>();
            var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in deviceCollection)
            {
                try
                {
                    var isDefault = device.ID == defaultDeviceId;
                    devices.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        Volume = device.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 0f,
                        Muted = device.AudioEndpointVolume?.Mute ?? false,
                        IsDefault = isDefault
                    });
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error getting device info for {device.ID}", ex);
                }
            }

            var sessions = new List<AudioSessionInfo>();
            if (defaultDevice != null)
            {
                try
                {
                    var sessionManager = defaultDevice.AudioSessionManager;
                    var sessionEnumerator = sessionManager.Sessions;

                    for (int i = 0; i < sessionEnumerator.Count; i++)
                    {
                        var session = sessionEnumerator[i];
                        try
                        {
                            sessions.Add(new AudioSessionInfo
                            {
                                Id = session.GetSessionIdentifier ?? i.ToString(),
                                Name = session.DisplayName ?? session.GetSessionIdentifier ?? "Unknown",
                                Volume = session.SimpleAudioVolume?.Volume ?? 0f,
                                Muted = session.SimpleAudioVolume?.Mute ?? false
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error getting session info", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Error enumerating audio sessions", ex);
                }
            }

            return new AudioState
            {
                DefaultOutputDeviceId = defaultDeviceId,
                Devices = devices,
                Sessions = sessions
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting audio state", ex);
            return new AudioState();
        }
    }

    public void SetDeviceVolume(string deviceId, float volume, bool muted)
    {
        try
        {
            var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioEndpointVolume != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
                device.AudioEndpointVolume.Mute = muted;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error setting device volume for {deviceId}", ex);
            throw;
        }
    }

    public void SetSessionVolume(string sessionId, float volume, bool muted)
    {
        try
        {
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice == null)
            {
                return;
            }

            var sessionManager = defaultDevice.AudioSessionManager;
            var sessionEnumerator = sessionManager.Sessions;

            for (int i = 0; i < sessionEnumerator.Count; i++)
            {
                var session = sessionEnumerator[i];
                var id = session.GetSessionIdentifier ?? i.ToString();
                if (id == sessionId)
                {
                    if (session.SimpleAudioVolume != null)
                    {
                        session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                        session.SimpleAudioVolume.Mute = muted;
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error setting session volume for {sessionId}", ex);
            throw;
        }
    }

    public void SetDefaultOutputDevice(string deviceId)
    {
        // TODO: Setting default output device requires more complex Windows API calls
        // For now, log a warning
        _logger.Warn($"Setting default output device is not fully implemented. Requested device: {deviceId}");
    }
}

