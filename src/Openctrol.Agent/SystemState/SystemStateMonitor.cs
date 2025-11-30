using System.Runtime.InteropServices;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.SystemState;

public sealed class SystemStateMonitor : ISystemStateMonitor, IDisposable
{
    private readonly ILogger _logger;
    private SystemStateSnapshot _currentSnapshot;
    private readonly System.Threading.Timer _pollTimer;
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<SystemStateSnapshot>? StateChanged;

    public SystemStateMonitor(ILogger logger)
    {
        _logger = logger;
        try
        {
            _currentSnapshot = DetectState();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to detect initial system state, defaulting to safe state", ex);
            _currentSnapshot = new SystemStateSnapshot
            {
                ActiveSessionId = 0,
                DesktopState = DesktopState.Unknown
            };
        }
        _pollTimer = new System.Threading.Timer(OnPoll, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public SystemStateSnapshot GetCurrent()
    {
        lock (_lock)
        {
            return _currentSnapshot;
        }
    }

    private void OnPoll(object? state)
    {
        // Prevent callbacks after disposal
        if (_disposed)
        {
            return;
        }

        var newSnapshot = DetectState();
        bool changed = false;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (newSnapshot.ActiveSessionId != _currentSnapshot.ActiveSessionId ||
                newSnapshot.DesktopState != _currentSnapshot.DesktopState)
            {
                _currentSnapshot = newSnapshot;
                changed = true;
            }
        }

        if (changed && !_disposed)
        {
            _logger.Info($"System state changed: Session={newSnapshot.ActiveSessionId}, State={newSnapshot.DesktopState}");
            StateChanged?.Invoke(this, newSnapshot);
        }
    }

    private SystemStateSnapshot DetectState()
    {
        int activeSessionId;
        try
        {
            activeSessionId = (int)WTSGetActiveConsoleSessionId();
            // Check if the session ID is valid (not SESSION_ID_NONE)
            if (activeSessionId == unchecked((int)SESSION_ID_NONE))
            {
                // No active console session
                activeSessionId = unchecked((int)SESSION_ID_NONE);
            }
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger.Warn($"WTSGetActiveConsoleSessionId not available, treating as no active console session: {ex.Message}");
            activeSessionId = unchecked((int)SESSION_ID_NONE);
        }
        catch (DllNotFoundException ex)
        {
            _logger.Warn($"kernel32.dll not found, treating as no active console session: {ex.Message}");
            activeSessionId = unchecked((int)SESSION_ID_NONE);
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting active console session ID, treating as no active console session", ex);
            activeSessionId = unchecked((int)SESSION_ID_NONE);
        }

        DesktopState desktopState;
        try
        {
            desktopState = DetectDesktopState();
        }
        catch (Exception ex)
        {
            _logger.Error("Error detecting desktop state", ex);
            desktopState = DesktopState.Unknown;
        }

        return new SystemStateSnapshot
        {
            ActiveSessionId = activeSessionId,
            DesktopState = desktopState
        };
    }

    private DesktopState DetectDesktopState()
    {
        try
        {
            // Check if we're on the login screen (Winlogon desktop)
            var hDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_READOBJECTS);
            if (hDesktop != IntPtr.Zero)
            {
                CloseDesktop(hDesktop);
                return DesktopState.LoginScreen;
            }

            // Check if desktop is locked
            var hCurrentDesktop = GetThreadDesktop(GetCurrentThreadId());
            if (hCurrentDesktop != IntPtr.Zero)
            {
                var desktopName = GetDesktopName(hCurrentDesktop);
                if (desktopName == "Screen-saver" || desktopName == "Winlogon")
                {
                    return DesktopState.Locked;
                }
            }

            // Check if session is locked using SystemParametersInfo
            var isLocked = false;
            SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, ref isLocked, 0);
            if (isLocked)
            {
                return DesktopState.Locked;
            }

            // Default to Desktop
            return DesktopState.Desktop;
        }
        catch (Exception ex)
        {
            _logger.Error("Error detecting desktop state", ex);
            return DesktopState.Unknown;
        }
    }

    private string GetDesktopName(IntPtr hDesktop)
    {
        try
        {
            // Use GetUserObjectInformation to retrieve desktop name
            var nameBuffer = new System.Text.StringBuilder(256);
            var nameLength = (uint)(nameBuffer.Capacity * sizeof(char));
            
            if (GetUserObjectInformation(hDesktop, UOI_NAME, nameBuffer, nameLength, out var requiredLength))
            {
                return nameBuffer.ToString();
            }
            
            // Fallback if API call fails
            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    // P/Invoke declarations
    private const uint SESSION_ID_NONE = 0xFFFFFFFF;
    
    // WTSGetActiveConsoleSessionId is in kernel32.dll (not wtsapi32.dll)
    // It may not be available on all Windows versions, so we handle EntryPointNotFoundException
    [DllImport("kernel32.dll", SetLastError = false, EntryPoint = "WTSGetActiveConsoleSessionId", ExactSpelling = false, CallingConvention = CallingConvention.Winapi)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("user32.dll")]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj,
        int nIndex,
        [Out] System.Text.StringBuilder pvInfo,
        uint nLength,
        out uint lpnLengthNeeded);

    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;
    private const int UOI_NAME = 2; // GetUserObjectInformation index for object name

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pollTimer?.Dispose();
        _disposed = true;
    }
}

