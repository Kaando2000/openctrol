using System.Runtime.InteropServices;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.SystemState;

public sealed class SystemStateMonitor : ISystemStateMonitor
{
    private readonly ILogger _logger;
    private SystemStateSnapshot _currentSnapshot;
    private readonly System.Threading.Timer _pollTimer;
    private readonly object _lock = new();

    public event EventHandler<SystemStateSnapshot>? StateChanged;

    public SystemStateMonitor(ILogger logger)
    {
        _logger = logger;
        _currentSnapshot = DetectState();
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
        var newSnapshot = DetectState();
        bool changed = false;

        lock (_lock)
        {
            if (newSnapshot.ActiveSessionId != _currentSnapshot.ActiveSessionId ||
                newSnapshot.DesktopState != _currentSnapshot.DesktopState)
            {
                _currentSnapshot = newSnapshot;
                changed = true;
            }
        }

        if (changed)
        {
            _logger.Info($"System state changed: Session={newSnapshot.ActiveSessionId}, State={newSnapshot.DesktopState}");
            StateChanged?.Invoke(this, newSnapshot);
        }
    }

    private SystemStateSnapshot DetectState()
    {
        var activeSessionId = (int)WTSGetActiveConsoleSessionId();
        var desktopState = DetectDesktopState();

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
        // Desktop name detection is complex, for now return a default
        // In a full implementation, you'd use GetUserObjectInformation
        return "Default";
    }

    // P/Invoke declarations
    [DllImport("wtsapi32.dll", SetLastError = true)]
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

    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;
}

