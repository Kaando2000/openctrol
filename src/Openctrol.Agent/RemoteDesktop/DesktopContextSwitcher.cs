using System.Runtime.InteropServices;
using Openctrol.Agent.SystemState;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.RemoteDesktop;

/// <summary>
/// Utility class for switching thread desktop context to enable interaction with
/// the active user session (Session 1+) or Secure Desktop (Winlogon) from Session 0.
/// 
/// This is critical for Windows Services running in Session 0 to interact with
/// the login screen, UAC prompts, and user desktop.
/// </summary>
public sealed class DesktopContextSwitcher : IDisposable
{
    private readonly ILogger _logger;
    private IntPtr _originalDesktop = IntPtr.Zero;
    private IntPtr _currentDesktop = IntPtr.Zero;
    private bool _isSwitched = false;
    private bool _disposed = false;

    public DesktopContextSwitcher(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Switches the current thread to the active input desktop.
    /// This must be called before any capture or input operations.
    /// </summary>
    /// <param name="systemState">Optional system state snapshot to determine which desktop to use</param>
    /// <returns>True if desktop context was successfully switched</returns>
    public bool SwitchToActiveDesktop(SystemStateSnapshot? systemState = null)
    {
        if (_disposed)
        {
            return false;
        }

        // If already switched, don't switch again (unless we need to change desktops)
        if (_isSwitched && _currentDesktop != IntPtr.Zero)
        {
            return true;
        }

        try
        {
            // Save original desktop
            _originalDesktop = GetThreadDesktop(GetCurrentThreadId());
            if (_originalDesktop == IntPtr.Zero)
            {
                _logger.Warn("[DesktopContext] Failed to get current thread desktop");
                return false;
            }

            // Determine which desktop to use based on system state
            bool useUserSession = false;
            bool useSecureDesktop = false;

            if (systemState != null)
            {
                // Use user session if we have an active session and it's not the system session
                if (systemState.ActiveSessionId > 0 &&
                    systemState.ActiveSessionId != unchecked((int)0xFFFFFFFF))
                {
                    if (systemState.DesktopState == DesktopState.Desktop)
                    {
                        useUserSession = true;
                    }
                    else if (systemState.DesktopState == DesktopState.LoginScreen ||
                             systemState.DesktopState == DesktopState.Locked)
                    {
                        useSecureDesktop = true;
                    }
                }
            }
            else
            {
                // If no system state available, try user session by default
                useUserSession = true;
            }

            // Try to open the input desktop first (most reliable for active user session)
            _currentDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS);
            
            if (_currentDesktop == IntPtr.Zero)
            {
                int lastError = Marshal.GetLastWin32Error();
                _logger.Debug($"[DesktopContext] OpenInputDesktop failed (error: {lastError}), trying alternatives");

                if (useUserSession)
                {
                    // Try user session desktop
                    _currentDesktop = OpenDesktop("Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS);
                    if (_currentDesktop == IntPtr.Zero)
                    {
                        // Try WinSta0\Default (user session desktop)
                        _currentDesktop = OpenDesktop("WinSta0\\Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS);
                    }
                }

                // Fallback to Winlogon for login screen or if user session failed
                if (_currentDesktop == IntPtr.Zero || useSecureDesktop)
                {
                    IntPtr secureDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS);
                    if (secureDesktop != IntPtr.Zero)
                    {
                        // Close previous desktop if we opened one
                        if (_currentDesktop != IntPtr.Zero)
                        {
                            CloseDesktop(_currentDesktop);
                        }
                        _currentDesktop = secureDesktop;
                    }
                }
            }

            if (_currentDesktop == IntPtr.Zero)
            {
                _logger.Warn("[DesktopContext] Failed to open any desktop (may need user token impersonation)");
                _originalDesktop = IntPtr.Zero;
                return false;
            }

            // Switch thread to the target desktop
            if (!SetThreadDesktop(_currentDesktop))
            {
                int lastError = Marshal.GetLastWin32Error();
                _logger.Warn($"[DesktopContext] SetThreadDesktop failed (error: {lastError})");
                CloseDesktop(_currentDesktop);
                _currentDesktop = IntPtr.Zero;
                _originalDesktop = IntPtr.Zero;
                return false;
            }

            _isSwitched = true;
            _logger.Debug("[DesktopContext] Successfully switched to active desktop");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("[DesktopContext] Error switching desktop context", ex);
            if (_currentDesktop != IntPtr.Zero)
            {
                try
                {
                    CloseDesktop(_currentDesktop);
                }
                catch { }
                _currentDesktop = IntPtr.Zero;
            }
            _originalDesktop = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>
    /// Restores the thread to its original desktop context.
    /// This should be called after capture/input operations are complete.
    /// </summary>
    public void RestoreOriginalDesktop()
    {
        if (!_isSwitched || _disposed)
        {
            return;
        }

        try
        {
            if (_originalDesktop != IntPtr.Zero)
            {
                SetThreadDesktop(_originalDesktop);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("[DesktopContext] Error restoring original desktop", ex);
        }
        finally
        {
            if (_currentDesktop != IntPtr.Zero)
            {
                try
                {
                    CloseDesktop(_currentDesktop);
                }
                catch { }
                _currentDesktop = IntPtr.Zero;
            }
            _originalDesktop = IntPtr.Zero;
            _isSwitched = false;
        }
    }

    /// <summary>
    /// Executes an action within the active desktop context.
    /// Automatically switches to the active desktop, executes the action, and restores the original desktop.
    /// </summary>
    public T? ExecuteInActiveDesktopContext<T>(Func<T> action, SystemStateSnapshot? systemState = null)
    {
        if (!SwitchToActiveDesktop(systemState))
        {
            _logger.Warn("[DesktopContext] Failed to switch to active desktop, action may fail");
        }

        try
        {
            return action();
        }
        finally
        {
            RestoreOriginalDesktop();
        }
    }

    /// <summary>
    /// Executes an action within the active desktop context (void version).
    /// </summary>
    public void ExecuteInActiveDesktopContext(Action action, SystemStateSnapshot? systemState = null)
    {
        if (!SwitchToActiveDesktop(systemState))
        {
            _logger.Warn("[DesktopContext] Failed to switch to active desktop, action may fail");
        }

        try
        {
            action();
        }
        finally
        {
            RestoreOriginalDesktop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        RestoreOriginalDesktop();
        _disposed = true;
    }

    // P/Invoke declarations
    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    // Desktop access rights
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    private const uint DESKTOP_WRITEOBJECTS = 0x0080;
}

