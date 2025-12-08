using System.Runtime.InteropServices;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Power;

public sealed class PowerManager : IPowerManager
{
    private readonly ILogger _logger;

    public PowerManager(ILogger logger)
    {
        _logger = logger;
        AcquireShutdownPrivilege();
    }

    public void Restart()
    {
        _logger.Info("Restarting system...");
        if (!InitiateSystemShutdownEx(
            null,
            "Openctrol Agent requested restart",
            0,
            true,
            true,
            SHUTDOWN_RESTART))
        {
            int error = Marshal.GetLastWin32Error();
            _logger.Error($"Failed to initiate system restart. Error code: {error}");
            throw new InvalidOperationException($"System restart failed with error code: {error}");
        }
    }

    public void Shutdown()
    {
        _logger.Info("Shutting down system...");
        if (!InitiateSystemShutdownEx(
            null,
            "Openctrol Agent requested shutdown",
            0,
            true,
            true,
            SHUTDOWN_POWEROFF))
        {
            int error = Marshal.GetLastWin32Error();
            _logger.Error($"Failed to initiate system shutdown. Error code: {error}");
            throw new InvalidOperationException($"System shutdown failed with error code: {error}");
        }
    }

    private void AcquireShutdownPrivilege()
    {
        try
        {
            IntPtr hToken;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
            {
                _logger.Warn("Failed to open process token for shutdown privilege");
                return;
            }

            try
            {
                var tkp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES[1]
                };

                if (LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out tkp.Privileges[0].Luid))
                {
                    tkp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
                    AdjustTokenPrivileges(hToken, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error acquiring shutdown privilege", ex);
        }
    }

    // P/Invoke declarations
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool InitiateSystemShutdownEx(
        string? lpMachineName,
        string lpMessage,
        uint dwTimeout,
        bool bForceAppsClosed,
        bool bRebootAfterShutdown,
        uint dwReason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
    private const uint SHUTDOWN_RESTART = 0x00000006;
    private const uint SHUTDOWN_POWEROFF = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }
}

