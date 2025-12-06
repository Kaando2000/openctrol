using System.Diagnostics;
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

            // Enumerate sessions from ALL render devices (not just default)
            // This gives us the actual device each session is routed to
            var sessions = new List<AudioSessionInfo>();
            var sessionIdsSeen = new HashSet<string>(); // Track to avoid duplicates
            
            foreach (var device in deviceCollection)
            {
                try
                {
                    var sessionManager = device.AudioSessionManager;
                    var sessionEnumerator = sessionManager.Sessions;

                    for (int i = 0; i < sessionEnumerator.Count; i++)
                    {
                        var session = sessionEnumerator[i];
                        try
                        {
                            var sessionId = session.GetSessionIdentifier ?? $"{device.ID}_{i}";
                            
                            // Skip if we've already seen this session (can appear on multiple devices in rare cases)
                            if (sessionIdsSeen.Contains(sessionId))
                            {
                                continue;
                            }
                            sessionIdsSeen.Add(sessionId);
                            
                            // The device we're enumerating from IS the device this session is routed to
                            // This is the OS-reported routing, not a cache
                            sessions.Add(new AudioSessionInfo
                            {
                                Id = sessionId,
                                Name = session.DisplayName ?? session.GetSessionIdentifier ?? "Unknown",
                                Volume = session.SimpleAudioVolume?.Volume ?? 0f,
                                Muted = session.SimpleAudioVolume?.Mute ?? false,
                                OutputDeviceId = device.ID // Actual device owning this session
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Error getting session info from device {device.ID}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error enumerating sessions from device {device.ID}: {ex.Message}");
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
            // Search for session across all devices (not just default)
            var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            
            foreach (var device in deviceCollection)
            {
                try
                {
                    var sessionManager = device.AudioSessionManager;
                    var sessionEnumerator = sessionManager.Sessions;

                    for (int i = 0; i < sessionEnumerator.Count; i++)
                    {
                        var session = sessionEnumerator[i];
                        var id = session.GetSessionIdentifier ?? $"{device.ID}_{i}";
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
                    _logger.Debug($"Error checking device {device.ID} for session {sessionId}: {ex.Message}");
                }
            }
            
            throw new ArgumentException($"Audio session not found: {sessionId}", nameof(sessionId));
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
            // Try multiple approaches for compatibility
            bool success = false;
            try
            {
                // Method 1: Try direct cast (works on some systems)
                var policyConfig = (IPolicyConfig)new PolicyConfigClient();
                int hr1 = policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
                int hr2 = policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
                
                if (hr1 == 0 && hr2 == 0)
                {
                    success = true;
                    _logger.Info($"Default output device set to: {device.FriendlyName}");
                }
                else
                {
                    _logger.Warn($"SetDefaultEndpoint returned HRESULT: Multimedia={hr1:X8}, Console={hr2:X8}");
                }
            }
            catch (InvalidCastException)
            {
                // Method 2: Try using Marshal.GetObjectForIUnknown
                try
                {
                    IntPtr pUnknown = Marshal.GetIUnknownForObject(new PolicyConfigClient());
                    var policyConfig = (IPolicyConfig)Marshal.GetObjectForIUnknown(pUnknown);
                    Marshal.Release(pUnknown);
                    
                    int hr1 = policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
                    int hr2 = policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
                    
                    if (hr1 == 0 && hr2 == 0)
                    {
                        success = true;
                        _logger.Info($"Default output device set to: {device.FriendlyName} (alternative method)");
                    }
                    else
                    {
                        _logger.Warn($"SetDefaultEndpoint (alt) returned HRESULT: Multimedia={hr1:X8}, Console={hr2:X8}");
                    }
                }
                catch (Exception altEx)
                {
                    _logger.Warn($"Alternative method failed: {altEx.Message}");
                }
            }
            catch (COMException comEx)
            {
                _logger.Warn($"COM error (HRESULT: {comEx.HResult:X8}): {comEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error setting default device: {ex.Message}");
            }
            
            if (!success)
            {
                // Method 3: Try PowerShell fallback (works on Windows 10+)
                try
                {
                    _logger.Info("Attempting PowerShell fallback for setting default audio device");
                    success = TrySetDefaultDeviceViaPowerShell(deviceId, device.FriendlyName);
                    if (success)
                    {
                        _logger.Info($"Default output device set to: {device.FriendlyName} (PowerShell method)");
                    }
                }
                catch (Exception psEx)
                {
                    _logger.Warn($"PowerShell fallback failed: {psEx.Message}");
                }
            }
            
            if (!success)
            {
                // Note: Default device change may require Windows settings or admin privileges
                // Some systems don't support programmatic default device changes
                _logger.Info($"Note: Default device change may require Windows settings or admin privileges. All methods (COM, PowerShell) failed.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error setting default output device: {deviceId}", ex);
            // Don't throw - return success with warning (some systems can't change default device)
            _logger.Warn($"Could not set default device (may require admin privileges or device limitation): {ex.Message}");
        }
    }

    private bool TrySetDefaultDeviceViaPowerShell(string deviceId, string deviceName)
    {
        try
        {
            // PowerShell fallback: Try using AudioDeviceCmdlets module if available
            // This module can be installed via: Install-Module -Name AudioDeviceCmdlets
            // If not available, this method will fail gracefully
            var psScript = $"-NoProfile -ExecutionPolicy Bypass -Command \"try {{ $devices = Get-AudioDevice -List; $device = $devices | Where-Object {{ $_.ID -eq '{deviceId}' }}; if ($device) {{ Set-AudioDevice -ID '{deviceId}'; Write-Output 'SUCCESS' }} else {{ Write-Output 'DEVICE_NOT_FOUND' }} }} catch {{ Write-Output 'MODULE_NOT_AVAILABLE' }}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = psScript,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                _logger.Debug("Failed to start PowerShell process for audio device change");
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            if (!process.WaitForExit(5000)) // 5 second timeout
            {
                process.Kill();
                _logger.Debug("PowerShell process timed out");
                return false;
            }

            if (process.ExitCode == 0 && output.Contains("SUCCESS"))
            {
                _logger.Info($"PowerShell successfully set default audio device to: {deviceName}");
                return true;
            }

            // Log the reason for failure
            if (output.Contains("MODULE_NOT_AVAILABLE"))
            {
                _logger.Debug("AudioDeviceCmdlets PowerShell module not available. Install via: Install-Module -Name AudioDeviceCmdlets");
            }
            else if (output.Contains("DEVICE_NOT_FOUND"))
            {
                _logger.Debug($"PowerShell could not find audio device with ID: {deviceId}");
            }
            else
            {
                _logger.Debug($"PowerShell method failed. Exit code: {process.ExitCode}, Output: {output}, Error: {error}");
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug($"PowerShell fallback exception: {ex.Message}");
            return false;
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
            AudioSessionControl? foundSession = null;
            string? foundSessionInstanceId = null;
            
            foreach (var dev in deviceCollection)
            {
                try
                {
                    var sessionManager = dev.AudioSessionManager;
                    var sessionEnumerator = sessionManager.Sessions;

                    for (int i = 0; i < sessionEnumerator.Count; i++)
                    {
                        var session = sessionEnumerator[i];
                        var id = session.GetSessionIdentifier ?? $"{dev.ID}_{i}";
                        if (id == sessionId)
                        {
                            foundSession = session;
                            foundSessionInstanceId = GetSessionInstanceIdentifier(session);
                            break;
                        }
                    }
                    if (foundSession != null) break;
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error checking device {dev.ID} for session {sessionId}: {ex.Message}");
                }
            }

            if (foundSession == null)
            {
                throw new ArgumentException($"Audio session not found: {sessionId}", nameof(sessionId));
            }

            if (string.IsNullOrEmpty(foundSessionInstanceId))
            {
                // Cannot route - session instance ID is required for per-app routing
                throw new NotSupportedException($"Per-app audio routing is not supported for session {sessionId}. The session does not provide a valid instance identifier.");
            }

            // Attempt to route session using Windows API
            // Note: SetDefaultEndpointForId is the correct API for per-app routing in Windows 10+
            // However, we cannot verify the routing actually took effect via the API
            // The routing will be reflected in GetState() on the next call if successful
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
            var hr = policyConfig.SetDefaultEndpointForId(foundSessionInstanceId, deviceId, Role.Multimedia);
            
            if (hr != 0)
            {
                // COM call failed - per-app routing may not be supported or may have failed
                throw new InvalidOperationException($"Failed to route session to device. Windows API returned error code: 0x{hr:X8}. Per-app audio routing may not be supported on this system.");
            }

            _logger.Info($"Session {sessionId} ({foundSession.DisplayName}) routing requested to device: {device.FriendlyName} ({deviceId}). Note: Routing cannot be verified via API and will be reflected in GetState() if successful.");
        }
        catch (COMException ex)
        {
            _logger.Error($"COM error routing session {sessionId} to device {deviceId}", ex);
            throw new InvalidOperationException($"Failed to route session to device: {ex.Message}. Per-app audio routing may not be supported.", ex);
        }
        catch (NotSupportedException)
        {
            // Re-throw NotSupportedException as-is
            throw;
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
            // For per-app routing via SetDefaultEndpointForId, we need the session instance identifier
            // This is typically the same as GetSessionIdentifier, but may need to be formatted differently
            // NAudio's AudioSessionControl wraps IAudioSessionControl2
            // The GetSessionIdentifier property should provide the identifier needed for routing
            var identifier = session.GetSessionIdentifier;
            if (string.IsNullOrEmpty(identifier))
            {
                return "";
            }
            
            // The identifier format expected by SetDefaultEndpointForId may vary
            // Return as-is and let the COM call handle validation
            return identifier;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Error getting session instance identifier: {ex.Message}");
            return "";
        }
    }
}


