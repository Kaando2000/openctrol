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
/// 
/// Uses token impersonation to escape Session 0 isolation and access the interactive user's desktop.
/// </summary>
public sealed class DesktopContextSwitcher : IDisposable
{
    private readonly ILogger _logger;
    private IntPtr _originalDesktop = IntPtr.Zero;
    private IntPtr _currentDesktop = IntPtr.Zero;
    private IntPtr _userToken = IntPtr.Zero;
    private bool _isSwitched = false;
    private bool _isImpersonating = false;
    private bool _disposed = false;

    public DesktopContextSwitcher(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Impersonates the active console user to escape Session 0 isolation.
    /// This must be called before attempting to access the user's desktop or input APIs.
    /// </summary>
    /// <returns>True if impersonation was successful</returns>
    private bool ImpersonateActiveUser()
    {
        if (_isImpersonating)
        {
            return true; // Already impersonating
        }

        try
        {
            // Step 1: Get the active console session ID
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF || sessionId == 0)
            {
                _logger.Debug($"[DesktopContext] No active console session found (sessionId: 0x{sessionId:X8})");
                return false; // No active console session
            }

            _logger.Debug($"[DesktopContext] Active console session ID: {sessionId}");

            // Step 2: Query the user token for this session
            IntPtr hToken = IntPtr.Zero;
            if (!WTSQueryUserToken(sessionId, out hToken))
            {
                int lastError = Marshal.GetLastWin32Error();
                _logger.Debug($"[DesktopContext] WTSQueryUserToken failed (error: {lastError})");
                return false;
            }

            if (hToken == IntPtr.Zero)
            {
                _logger.Debug("[DesktopContext] WTSQueryUserToken returned null token");
                return false;
            }

            // Step 3: Duplicate the token to create an impersonation token
            IntPtr hImpersonationToken = IntPtr.Zero;
            SECURITY_ATTRIBUTES tokenAttributes = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                lpSecurityDescriptor = IntPtr.Zero,
                bInheritHandle = false
            };

            if (!DuplicateTokenEx(
                hToken,
                TOKEN_QUERY | TOKEN_IMPERSONATE | TOKEN_DUPLICATE,
                ref tokenAttributes,
                SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TOKEN_TYPE.TokenImpersonation,
                out hImpersonationToken))
            {
                int lastError = Marshal.GetLastWin32Error();
                _logger.Debug($"[DesktopContext] DuplicateTokenEx failed (error: {lastError})");
                CloseHandle(hToken);
                return false;
            }

            // Close the original token (we have the duplicate now)
            CloseHandle(hToken);

            // Step 4: Impersonate the user by setting the thread token
            try
            {
                _userToken = hImpersonationToken;
                if (!SetThreadToken(IntPtr.Zero, hImpersonationToken))
                {
                    int lastError = Marshal.GetLastWin32Error();
                    _logger.Debug($"[DesktopContext] SetThreadToken failed (error: {lastError})");
                    CloseHandle(hImpersonationToken);
                    _userToken = IntPtr.Zero;
                    return false;
                }
                
                _isImpersonating = true;
                _logger.Debug("[DesktopContext] Successfully impersonated active console user");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug($"[DesktopContext] SetThreadToken failed: {ex.Message}");
                CloseHandle(hImpersonationToken);
                _userToken = IntPtr.Zero;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"[DesktopContext] Error during token impersonation: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reverts the thread impersonation and closes token handles.
    /// </summary>
    private void RevertImpersonation()
    {
        if (!_isImpersonating)
        {
            return;
        }

        try
        {
            // Revert impersonation by removing thread token
            if (_isImpersonating)
            {
                if (!SetThreadToken(IntPtr.Zero, IntPtr.Zero))
                {
                    // Fallback to RevertToSelf if SetThreadToken fails
                    RevertToSelf();
                }
            }
            else
            {
                // Fallback to RevertToSelf if we're not sure of state
                RevertToSelf();
            }

            // Close token handle
            if (_userToken != IntPtr.Zero)
            {
                CloseHandle(_userToken);
                _userToken = IntPtr.Zero;
            }

            _isImpersonating = false;
            _logger.Debug("[DesktopContext] Reverted user impersonation");
        }
        catch (Exception ex)
        {
            _logger.Debug($"[DesktopContext] Error reverting impersonation: {ex.Message}");
        }
    }

    /// <summary>
    /// Switches the current thread to the active input desktop.
    /// This must be called before any capture or input operations.
    /// Uses token impersonation to escape Session 0 isolation.
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
            // CRITICAL: Impersonate the active user BEFORE attempting to open desktop
            // This is required to escape Session 0 isolation
            if (!ImpersonateActiveUser())
            {
                _logger.Debug("[DesktopContext] Token impersonation failed, attempting desktop access anyway (may fail)");
            }

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
            // This should now work because we're impersonating the user
            _currentDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS);
            
            if (_currentDesktop == IntPtr.Zero)
            {
                int lastError = Marshal.GetLastWin32Error();
                _logger.Debug($"[DesktopContext] OpenInputDesktop failed (error: {lastError}) after impersonation, trying alternatives");

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
                _logger.Warn("[DesktopContext] Failed to open any desktop even after token impersonation");
                // Don't revert impersonation yet - it might be needed for other operations
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
            _logger.Error($"[DesktopContext] Error restoring original desktop: {ex.Message}", ex);
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
            
            // Note: We don't revert impersonation here - keep it active for subsequent operations
            // Impersonation will be reverted in Dispose()
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
        
        // Revert impersonation and clean up token handles
        RevertImpersonation();
        
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

    // Token impersonation P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        ref SECURITY_ATTRIBUTES lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
        TOKEN_TYPE TokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetThreadToken(IntPtr Thread, IntPtr Token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertToSelf();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Token access rights
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_DUPLICATE = 0x0002;

    // Security impersonation levels
    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous = 0,
        SecurityIdentification = 1,
        SecurityImpersonation = 2,
        SecurityDelegation = 3
    }

    // Token types
    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2
    }

    // Security attributes structure
    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }
}

