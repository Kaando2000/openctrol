using System.Runtime.InteropServices;
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
                            // Get the device ID this session is routed to
                            // Note: Windows audio APIs don't provide a reliable way to query which device
                            // a session is currently routed to. We can set routing via SetSessionOutputDevice(),
                            // but GetState() cannot accurately reflect the actual routing. For now, we
                            // report the default device ID, but clients should not rely on this field
                            // to determine actual routing - it's informational only.
                            var sessionDeviceId = defaultDeviceId;
                            
                            sessions.Add(new AudioSessionInfo
                            {
                                Id = session.GetSessionIdentifier ?? i.ToString(),
                                Name = session.DisplayName ?? session.GetSessionIdentifier ?? "Unknown",
                                Volume = session.SimpleAudioVolume?.Volume ?? 0f,
                                Muted = session.SimpleAudioVolume?.Mute ?? false,
                                OutputDeviceId = sessionDeviceId
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
        try
        {
            // Validate device exists
            var device = _deviceEnumerator.GetDevice(deviceId);
            if (device == null)
            {
                throw new ArgumentException($"Audio device not found: {deviceId}", nameof(deviceId));
            }

            // Verify it's an output device
            if (device.DataFlow != DataFlow.Render)
            {
                throw new ArgumentException($"Device {deviceId} is not an output device", nameof(deviceId));
            }

            // Use COM interface to set default device
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
            policyConfig.SetDefaultEndpoint(deviceId, Role.Console);

            _logger.Info($"Default output device set to: {device.FriendlyName} ({deviceId})");
        }
        catch (COMException ex)
        {
            _logger.Error($"COM error setting default output device: {deviceId}", ex);
            throw new InvalidOperationException($"Failed to set default output device: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error setting default output device: {deviceId}", ex);
            throw;
        }
    }

    public void SetSessionOutputDevice(string sessionId, string deviceId)
    {
        try
        {
            // Validate device exists
            var device = _deviceEnumerator.GetDevice(deviceId);
            if (device == null)
            {
                throw new ArgumentException($"Audio device not found: {deviceId}", nameof(deviceId));
            }

            // Verify it's an output device
            if (device.DataFlow != DataFlow.Render)
            {
                throw new ArgumentException($"Device {deviceId} is not an output device", nameof(deviceId));
            }

            // Find the session across all devices
            var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in deviceCollection)
            {
                try
                {
                    var sessionManager = dev.AudioSessionManager;
                    var sessionEnumerator = sessionManager.Sessions;

                    for (int i = 0; i < sessionEnumerator.Count; i++)
                    {
                        var session = sessionEnumerator[i];
                        var id = session.GetSessionIdentifier ?? i.ToString();
                        if (id == sessionId)
                        {
                            // Use COM interface to route session to device
                            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
                            // Get session instance identifier - NAudio wraps IAudioSessionControl2
                            // We need to access the underlying COM interface to get the session instance ID
                            var sessionInstanceId = GetSessionInstanceIdentifier(session);
                            if (!string.IsNullOrEmpty(sessionInstanceId))
                            {
                                policyConfig.SetDefaultEndpointForId(sessionInstanceId, deviceId, Role.Multimedia);
                                _logger.Info($"Session {sessionId} ({session.DisplayName}) routed to device: {device.FriendlyName} ({deviceId})");
                                return;
                            }
                            else
                            {
                                _logger.Warn($"Could not get session instance identifier for session {sessionId}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error checking device {dev.ID} for session {sessionId}: {ex.Message}");
                }
            }

            throw new ArgumentException($"Audio session not found: {sessionId}", nameof(sessionId));
        }
        catch (COMException ex)
        {
            _logger.Error($"COM error routing session {sessionId} to device {deviceId}", ex);
            throw new InvalidOperationException($"Failed to route session to device: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error routing session {sessionId} to device {deviceId}", ex);
            throw;
        }
    }

    // COM interfaces for setting default audio device
    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290190C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat(string pszDeviceName, out IntPtr ppFormat);

        [PreserveSig]
        int GetDeviceFormat(string pszDeviceName, bool bDefault, out IntPtr ppFormat);

        [PreserveSig]
        int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);

        [PreserveSig]
        int GetProcessingPeriod(string pszDeviceName, bool bDefault, out IntPtr pDefaultPeriod, out IntPtr pMinimumPeriod);

        [PreserveSig]
        int SetProcessingPeriod(string pszDeviceName, IntPtr pPeriod);

        [PreserveSig]
        int GetShareMode(string pszDeviceName, out IntPtr pShareMode);

        [PreserveSig]
        int SetShareMode(string pszDeviceName, IntPtr pShareMode);

        [PreserveSig]
        int GetPropertyValue(string pszDeviceName, IntPtr key, out IntPtr pv);

        [PreserveSig]
        int SetPropertyValue(string pszDeviceName, IntPtr key, IntPtr pv);

        [PreserveSig]
        int SetDefaultEndpoint(string pszDeviceName, Role role);

        [PreserveSig]
        int SetEndpointVisibility(string pszDeviceName, bool bVisible);

        [PreserveSig]
        int SetDefaultEndpointForId(string pszDeviceId, string pszDeviceIdDefault, Role role);
    }

    private string GetSessionInstanceIdentifier(AudioSessionControl session)
    {
        try
        {
            // NAudio's AudioSessionControl wraps IAudioSessionControl2
            // The session identifier is typically the same as GetSessionIdentifier for routing purposes
            // For per-app routing, we use the session identifier which uniquely identifies the session
            return session.GetSessionIdentifier ?? "";
        }
        catch (Exception ex)
        {
            _logger.Debug($"Error getting session instance identifier: {ex.Message}");
            return "";
        }
    }
}

