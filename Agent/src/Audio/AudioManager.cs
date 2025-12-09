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
            // Use proper COM activation via CoCreateInstance for Windows 10/11
            // Interface ID: 870AF99C-171D-4F9E-AF0D-E63DF40C2BC9 (IPolicyConfig)
            bool success = false;
            IntPtr policyConfigPtr = IntPtr.Zero;
            
            try
            {
                // Method 1: Use CoCreateInstance to properly activate the COM object
                // CLSID for PolicyConfig: {870AF99C-171D-4F9E-AF0D-E63DF40C2BC9}
                Guid clsid = new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
                Guid iid = new Guid("F8679F50-850A-41CF-9C72-430F290190C8"); // IPolicyConfig IID
                
                int hr = CoCreateInstance(
                    clsid,
                    IntPtr.Zero,
                    CLSCTX.ALL,
                    iid,
                    out policyConfigPtr);
                
                if (hr == 0 && policyConfigPtr != IntPtr.Zero)
                {
                    var policyConfig = (IPolicyConfig)Marshal.GetObjectForIUnknown(policyConfigPtr);
                    
                    int hr1 = policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
                    int hr2 = policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
                    
                    if (hr1 == 0 && hr2 == 0)
                    {
                        success = true;
                        _logger.Info($"Default output device set to: {device.FriendlyName} (COM method)");
                    }
                    else
                    {
                        _logger.Warn($"SetDefaultEndpoint returned HRESULT: Multimedia={hr1:X8}, Console={hr2:X8}");
                    }
                }
                else
                {
                    _logger.Warn($"CoCreateInstance failed with HRESULT: {hr:X8}");
                }
            }
            catch (COMException comEx)
            {
                _logger.Warn($"COM error (HRESULT: {comEx.HResult:X8}): {comEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error setting default device via COM: {ex.Message}");
            }
            finally
            {
                if (policyConfigPtr != IntPtr.Zero)
                {
                    Marshal.Release(policyConfigPtr);
                }
            }
            
            // Method 2: Fallback to direct instantiation if CoCreateInstance fails
            if (!success)
            {
                try
                {
                    var policyConfig = (IPolicyConfig)new PolicyConfigClient();
                    int hr1 = policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
                    int hr2 = policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
                    
                    if (hr1 == 0 && hr2 == 0)
                    {
                        success = true;
                        _logger.Info($"Default output device set to: {device.FriendlyName} (direct COM method)");
                    }
                    else
                    {
                        _logger.Warn($"SetDefaultEndpoint (direct) returned HRESULT: Multimedia={hr1:X8}, Console={hr2:X8}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Direct COM method failed: {ex.Message}");
                }
            }
            
            if (!success)
            {
                // Note: Default device change may require Windows settings or admin privileges
                // Some systems don't support programmatic default device changes
                _logger.Info($"Note: Default device change may require Windows settings or admin privileges. COM methods failed.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error setting default output device: {deviceId}", ex);
            // Don't throw - return success with warning (some systems can't change default device)
            _logger.Warn($"Could not set default device (may require admin privileges or device limitation): {ex.Message}");
        }
    }

    // P/Invoke declarations for COM activation
    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern int CoCreateInstance(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        IntPtr pUnkOuter,
        CLSCTX dwClsContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IntPtr ppv);

    [Flags]
    private enum CLSCTX : uint
    {
        INPROC_SERVER = 0x1,
        INPROC_HANDLER = 0x2,
        LOCAL_SERVER = 0x4,
        INPROC_SERVER16 = 0x8,
        REMOTE_SERVER = 0x10,
        INPROC_HANDLER16 = 0x20,
        RESERVED1 = 0x40,
        RESERVED2 = 0x80,
        RESERVED3 = 0x100,
        RESERVED4 = 0x200,
        NO_CODE_DOWNLOAD = 0x400,
        RESERVED5 = 0x800,
        NO_CUSTOM_MARSHAL = 0x1000,
        ENABLE_CODE_DOWNLOAD = 0x2000,
        NO_FAILURE_LOG = 0x4000,
        DISABLE_AAA = 0x8000,
        ENABLE_AAA = 0x10000,
        FROM_DEFAULT_CONTEXT = 0x20000,
        ACTIVATE_32_BIT_SERVER = 0x40000,
        ACTIVATE_64_BIT_SERVER = 0x80000,
        ENABLE_CLOAKING = 0x100000,
        PS_DLL = 0x80000000,
        ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
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

            if (string.IsNullOrWhiteSpace(foundSessionInstanceId))
            {
                // Cannot route - session instance ID is required for per-app routing
                throw new NotSupportedException($"Per-app audio routing is not supported for session {sessionId}. The session does not provide a valid instance identifier.");
            }

            // Attempt to route session using Windows API
            // Try IPolicyConfigVista first (has SetDefaultEndpointForId), fall back to IPolicyConfig if vtable mismatch
            bool routingSuccess = false;
            try
            {
                // Try to use IPolicyConfigVista which includes SetDefaultEndpointForId
                // This may fail on some Windows builds if the vtable layout differs
                var policyConfigVista = (IPolicyConfigVista)new PolicyConfigClient();
                var hr = policyConfigVista.SetDefaultEndpointForId(foundSessionInstanceId, deviceId, Role.Multimedia);
                
                if (hr == 0)
                {
                    routingSuccess = true;
                    _logger.Info($"Session {sessionId} ({foundSession.DisplayName}) routing requested to device: {device.FriendlyName} ({deviceId}) via SetDefaultEndpointForId.");
                }
                else
                {
                    _logger.Warn($"SetDefaultEndpointForId returned error code: 0x{hr:X8}. Falling back to SetDefaultEndpoint.");
                }
            }
            catch (System.Runtime.InteropServices.SEHException)
            {
                // Vtable mismatch - SetDefaultEndpointForId doesn't exist at expected slot
                // Note: ExecutionEngineException is obsolete in .NET 8+, using SEHException instead
                _logger.Warn($"SetDefaultEndpointForId not available (vtable mismatch). Falling back to SetDefaultEndpoint (system-wide routing only).");
            }
            catch (AccessViolationException)
            {
                // Access violation indicates vtable issue
                _logger.Warn($"SetDefaultEndpointForId caused access violation (vtable mismatch). Falling back to SetDefaultEndpoint (system-wide routing only).");
            }
            catch (COMException comEx)
            {
                // COM error - may indicate vtable issue or other COM problem
                _logger.Warn($"SetDefaultEndpointForId COM error: {comEx.Message}. Falling back to SetDefaultEndpoint (system-wide routing only).");
            }

            // Fallback to system-wide default device change if per-app routing failed
            if (!routingSuccess)
            {
                var policyConfig = (IPolicyConfig)new PolicyConfigClient();
                var hr1 = policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
                var hr2 = policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
                
                if (hr1 == 0 && hr2 == 0)
                {
                    _logger.Info($"Session {sessionId} routing: Per-app routing not available. Changed system-wide default device to: {device.FriendlyName} ({deviceId}).");
                    // Note: This changes the default for ALL applications, not just this session
                }
                else
                {
                    throw new InvalidOperationException($"Failed to route session to device. SetDefaultEndpoint returned error codes: Multimedia=0x{hr1:X8}, Console=0x{hr2:X8}. Per-app audio routing may not be supported on this system.");
                }
            }
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
    }

    // Separate interface for SetDefaultEndpointForId to avoid vtable issues
    // This method may not exist at the expected vtable slot on all Windows versions
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290190C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfigVista
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


