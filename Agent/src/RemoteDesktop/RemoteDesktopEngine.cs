using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Openctrol.Agent.Config;
using Openctrol.Agent.Input;
using Openctrol.Agent.SystemState;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.RemoteDesktop;

public sealed class RemoteDesktopEngine : IRemoteDesktopEngine
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly InputDispatcher _inputDispatcher;
    private readonly ISystemStateMonitor? _systemStateMonitor;
    private readonly List<IFrameSubscriber> _subscribers = new();
    private readonly object _subscribersLock = new();
    private readonly object _statusLock = new(); // Lock for status fields accessed from multiple threads
    private Thread? _captureThread;
    private CancellationTokenSource? _cancellationTokenSource;
    private CrossSessionCaptureContext? _captureContext;
    private DesktopContextSwitcher? _desktopContextSwitcher;
    private volatile bool _isRunning;
    private DateTimeOffset _lastFrameAt;
    private long _sequenceNumber;
    private string _currentState = "unknown";
    private string _currentMonitorId = "DISPLAY1";
    private int _captureFailureCount = 0;
    private const int MaxCaptureFailures = 5;
    private bool _isDegraded = false; // Set to true when capture repeatedly fails

    public RemoteDesktopEngine(
        IConfigManager configManager,
        ILogger logger,
        InputDispatcher inputDispatcher,
        ISystemStateMonitor? systemStateMonitor = null)
    {
        _configManager = configManager;
        _logger = logger;
        _inputDispatcher = inputDispatcher;
        _systemStateMonitor = systemStateMonitor;
    }

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _captureContext = new CrossSessionCaptureContext(_logger);
        _desktopContextSwitcher = new DesktopContextSwitcher(_logger);
        _logger.Info("[RemoteDesktop] Starting with cross-session capture support...");

        // Hook into system state monitor to react to state changes
        if (_systemStateMonitor != null)
        {
            _systemStateMonitor.StateChanged += OnSystemStateChanged;
            // Update current state from initial system state
            var initialState = _systemStateMonitor.GetCurrent();
            UpdateStateFromSystemState(initialState);
        }

        _captureThread = new Thread(() => CaptureLoop(_cancellationTokenSource.Token))
        {
            IsBackground = true,
            Name = "RemoteDesktopCapture"
        };
        _captureThread.Start();

        _logger.Info("[RemoteDesktop] Started");
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _logger.Info("[RemoteDesktop] Stopping...");

        // Cancel capture loop
        _cancellationTokenSource?.Cancel();

        // Unhook from system state monitor
        if (_systemStateMonitor != null)
        {
            _systemStateMonitor.StateChanged -= OnSystemStateChanged;
        }

        // Wait for capture thread to exit
        if (_captureThread != null)
        {
            if (!_captureThread.Join(TimeSpan.FromSeconds(5)))
            {
                _logger.Warn("[RemoteDesktop] Capture thread did not exit within timeout");
            }
            _captureThread = null;
        }

        // Dispose resources
        _captureContext?.Dispose();
        _captureContext = null;
        _desktopContextSwitcher?.Dispose();
        _desktopContextSwitcher = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _logger.Info("[RemoteDesktop] Stopped");
    }

    public RemoteDesktopStatus GetStatus()
    {
        // Synchronize access to status fields that are written from multiple threads
        lock (_statusLock)
        {
            var state = _currentState;
            // If degraded, append degraded status
            if (_isDegraded && state != "unknown")
            {
                state = $"{state}_degraded";
            }
            
            return new RemoteDesktopStatus
            {
                IsRunning = _isRunning,
                LastFrameAt = _lastFrameAt,
                State = state,
                IsDegraded = _isDegraded
            };
        }
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        
        try
        {
            // CRITICAL: Switch to active desktop context before enumerating monitors
            // This ensures EnumDisplayMonitors is called within the context of the active console session
            SystemStateSnapshot? systemState = null;
            if (_systemStateMonitor != null)
            {
                systemState = _systemStateMonitor.GetCurrent();
            }

            // Enumerate monitors within the active desktop context
            List<MonitorInfo> win32Monitors;
            if (_desktopContextSwitcher != null)
            {
                win32Monitors = _desktopContextSwitcher.ExecuteInActiveDesktopContext(
                    () => EnumerateMonitorsWin32(),
                    systemState) ?? new List<MonitorInfo>();
            }
            else
            {
                win32Monitors = EnumerateMonitorsWin32();
            }
            if (win32Monitors.Count > 0)
            {
                monitors.AddRange(win32Monitors);
                _logger.Info($"[RemoteDesktop] Win32 EnumDisplayMonitors found {win32Monitors.Count} monitor(s)");
                foreach (var m in win32Monitors)
                {
                    _logger.Info($"[RemoteDesktop]   Monitor: {m.Id} = {m.Name} ({m.Width}x{m.Height}) at ({m.X},{m.Y}), Primary={m.IsPrimary}");
                }
            }
            
            // ALWAYS also try Screen.AllScreens to supplement - it may see displays differently
            // This should also be called within the active desktop context
            try
            {
                System.Windows.Forms.Screen[] screens;
                if (_desktopContextSwitcher != null)
                {
                    screens = _desktopContextSwitcher.ExecuteInActiveDesktopContext(
                        () => System.Windows.Forms.Screen.AllScreens,
                        systemState) ?? Array.Empty<System.Windows.Forms.Screen>();
                }
                else
                {
                    screens = System.Windows.Forms.Screen.AllScreens;
                }
                _logger.Debug($"[RemoteDesktop] Screen.AllScreens found {screens.Length} screen(s)");
                
                foreach (var screen in screens)
                {
                    // Check if we already have this monitor (match by position and size, or device name)
                    var existingMonitor = monitors.FirstOrDefault(m => 
                        (m.X == screen.Bounds.X && m.Y == screen.Bounds.Y && 
                         m.Width == screen.Bounds.Width && m.Height == screen.Bounds.Height) ||
                        (!string.IsNullOrEmpty(screen.DeviceName) && m.Name == screen.DeviceName));
                    
                    if (existingMonitor == null)
                    {
                        // New monitor found from Screen.AllScreens - add it
                        monitors.Add(new MonitorInfo
                        {
                            Id = $"DISPLAY{monitors.Count + 1}",
                            Name = screen.DeviceName ?? $"Display {monitors.Count + 1}",
                            Width = screen.Bounds.Width,
                            Height = screen.Bounds.Height,
                            X = screen.Bounds.X,
                            Y = screen.Bounds.Y,
                            IsPrimary = screen.Primary
                        });
                        _logger.Debug($"[RemoteDesktop] Added monitor from Screen.AllScreens: {screen.DeviceName} ({screen.Bounds.Width}x{screen.Bounds.Height}) at ({screen.Bounds.X},{screen.Bounds.Y})");
                    }
                    else
                    {
                        // Update existing monitor with better info if available
                        var index = monitors.IndexOf(existingMonitor);
                        if (index >= 0 && !string.IsNullOrEmpty(screen.DeviceName) && string.IsNullOrEmpty(monitors[index].Name))
                        {
                            monitors[index] = new MonitorInfo
                            {
                                Id = monitors[index].Id,
                                Name = screen.DeviceName,
                                Width = monitors[index].Width,
                                Height = monitors[index].Height,
                                X = monitors[index].X,
                                Y = monitors[index].Y,
                                IsPrimary = monitors[index].IsPrimary || screen.Primary
                            };
                        }
                    }
                }
            }
            catch (Exception screenEx)
            {
                _logger.Debug($"[RemoteDesktop] Screen.AllScreens failed (non-critical): {screenEx.Message}");
            }
            
            // Remove duplicates based on position and size, but be more lenient to avoid false duplicates
            var uniqueMonitors = new List<MonitorInfo>();
            foreach (var monitor in monitors)
            {
                // More lenient duplicate detection - check device name first, then position/size
                bool isDuplicate = uniqueMonitors.Any(existing => 
                {
                    // If device names match (and are not empty), it's the same monitor
                    if (!string.IsNullOrEmpty(existing.Name) && !string.IsNullOrEmpty(monitor.Name) &&
                        existing.Name.Equals(monitor.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    // Otherwise check position and size (with tolerance)
                    return Math.Abs(existing.X - monitor.X) < 10 && 
                           Math.Abs(existing.Y - monitor.Y) < 10 &&
                           Math.Abs(existing.Width - monitor.Width) < 10 && 
                           Math.Abs(existing.Height - monitor.Height) < 10;
                });
                
                if (!isDuplicate)
                {
                    uniqueMonitors.Add(monitor);
                }
                else
                {
                    // Update existing monitor with better name if available
                    var existing = uniqueMonitors.FirstOrDefault(e => 
                        (!string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(monitor.Name) &&
                         e.Name.Equals(monitor.Name, StringComparison.OrdinalIgnoreCase)) ||
                        (Math.Abs(e.X - monitor.X) < 10 && Math.Abs(e.Y - monitor.Y) < 10 &&
                         Math.Abs(e.Width - monitor.Width) < 10 && Math.Abs(e.Height - monitor.Height) < 10));
                    if (existing != null)
                    {
                        var index = uniqueMonitors.IndexOf(existing);
                        // Update with better name if current name is generic
                        if (string.IsNullOrEmpty(uniqueMonitors[index].Name) || 
                            uniqueMonitors[index].Name.StartsWith("Display ") ||
                            uniqueMonitors[index].Name.StartsWith("Monitor_"))
                        {
                            if (!string.IsNullOrEmpty(monitor.Name) && 
                                !monitor.Name.StartsWith("Display ") &&
                                !monitor.Name.StartsWith("Monitor_"))
                            {
                                uniqueMonitors[index] = new MonitorInfo
                                {
                                    Id = uniqueMonitors[index].Id,
                                    Name = monitor.Name,
                                    Width = uniqueMonitors[index].Width,
                                    Height = uniqueMonitors[index].Height,
                                    X = uniqueMonitors[index].X,
                                    Y = uniqueMonitors[index].Y,
                                    IsPrimary = uniqueMonitors[index].IsPrimary || monitor.IsPrimary
                                };
                            }
                        }
                    }
                }
            }
            
            // Sort by primary first, then by position
            uniqueMonitors = uniqueMonitors.OrderBy(m => m.IsPrimary ? 0 : 1)
                                          .ThenBy(m => m.X)
                                          .ThenBy(m => m.Y)
                                          .ToList();
            
            // Reassign IDs in sorted order
            for (int i = 0; i < uniqueMonitors.Count; i++)
            {
                uniqueMonitors[i] = new MonitorInfo
                {
                    Id = $"DISPLAY{i + 1}",
                    Name = uniqueMonitors[i].Name,
                    Width = uniqueMonitors[i].Width,
                    Height = uniqueMonitors[i].Height,
                    X = uniqueMonitors[i].X,
                    Y = uniqueMonitors[i].Y,
                    IsPrimary = uniqueMonitors[i].IsPrimary
                };
            }
            
            monitors = uniqueMonitors;
            
            if (monitors.Count > 0)
            {
                _logger.Info($"[RemoteDesktop] Final enumeration: {monitors.Count} unique monitor(s) detected");
                foreach (var monitor in monitors)
                {
                    _logger.Info($"[RemoteDesktop]   - {monitor.Id}: {monitor.Name} ({monitor.Width}x{monitor.Height}) at ({monitor.X},{monitor.Y}), Primary={monitor.IsPrimary}");
                }
                return monitors;
            }
            else
            {
                _logger.Warn("[RemoteDesktop] No monitors detected after all enumeration methods. This may indicate a service session limitation.");
            }
            
            // Fallback: Try Screen.AllScreens as standalone method
            try
            {
                System.Windows.Forms.Screen[] screens;
                if (_desktopContextSwitcher != null)
                {
                    screens = _desktopContextSwitcher.ExecuteInActiveDesktopContext(
                        () => System.Windows.Forms.Screen.AllScreens,
                        systemState) ?? Array.Empty<System.Windows.Forms.Screen>();
                }
                else
                {
                    screens = System.Windows.Forms.Screen.AllScreens;
                }
                if (screens.Length > 0)
                {
                    for (int i = 0; i < screens.Length; i++)
                    {
                        var screen = screens[i];
                        monitors.Add(new MonitorInfo
                        {
                            Id = $"DISPLAY{i + 1}",
                            Name = screen.DeviceName ?? $"Display {i + 1}",
                            Width = screen.Bounds.Width,
                            Height = screen.Bounds.Height,
                            X = screen.Bounds.X,
                            Y = screen.Bounds.Y,
                            IsPrimary = screen.Primary
                        });
                    }
                    
                    if (monitors.Count > 0)
                    {
                        _logger.Info($"[RemoteDesktop] Screen.AllScreens fallback found {monitors.Count} monitor(s)");
                        return monitors;
                    }
                }
            }
            catch (Exception screenEx)
            {
                _logger.Warn($"[RemoteDesktop] Screen.AllScreens fallback failed: {screenEx.Message}");
            }
            
            // Last resort fallback to single monitor
            _logger.Warn("[RemoteDesktop] All monitor enumeration methods failed, using single monitor fallback");
            monitors.Add(new MonitorInfo
            {
                Id = "DISPLAY1",
                Name = "Primary Display",
                Width = GetSystemMetrics(SM_CXSCREEN),
                Height = GetSystemMetrics(SM_CYSCREEN),
                X = 0,
                Y = 0,
                IsPrimary = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Error enumerating monitors", ex);
            // Fallback to single monitor
            monitors.Add(new MonitorInfo
            {
                Id = "DISPLAY1",
                Name = "Primary Display",
                Width = GetSystemMetrics(SM_CXSCREEN),
                Height = GetSystemMetrics(SM_CYSCREEN),
                X = 0,
                Y = 0,
                IsPrimary = true
            });
        }

        // Always log final monitor count
        _logger.Info($"[RemoteDesktop] Final monitor enumeration result: {monitors.Count} monitor(s)");
        return monitors;
    }

    private List<MonitorInfo> EnumerateMonitorsWin32()
    {
        var monitors = new List<MonitorInfo>();
        var monitorDataList = new List<MonitorEnumData>();

        try
        {
            // Use EnumDisplayMonitors to get all active monitors from the active console session
            // This API works across sessions when called from a service running as LocalSystem
            bool enumResult = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                MONITORINFOEX monitorInfo = new MONITORINFOEX
                {
                    Size = Marshal.SizeOf(typeof(MONITORINFOEX)),
                    DeviceName = new string('\0', 32) // Initialize with null characters
                };
                
                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    // Trim null characters from device name
                    string deviceName = monitorInfo.DeviceName?.TrimEnd('\0') ?? $"Monitor_{monitorDataList.Count + 1}";
                    
                    // Calculate width and height from bounds
                    int width = monitorInfo.Monitor.Right - monitorInfo.Monitor.Left;
                    int height = monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top;
                    
                    // Only add if we have valid dimensions
                    if (width > 0 && height > 0)
                    {
                        monitorDataList.Add(new MonitorEnumData
                        {
                            DeviceName = deviceName,
                            Bounds = monitorInfo.Monitor,
                            IsPrimary = (monitorInfo.Flags & MONITORINFOF_PRIMARY) != 0
                        });
                        
                        _logger.Debug($"[RemoteDesktop] EnumDisplayMonitors found monitor: {deviceName} ({width}x{height}) at ({monitorInfo.Monitor.Left},{monitorInfo.Monitor.Top}), Primary={((monitorInfo.Flags & MONITORINFOF_PRIMARY) != 0)}");
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (!enumResult)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.Warn($"[RemoteDesktop] EnumDisplayMonitors failed with error code: {error}");
            }
            else if (monitorDataList.Count > 0)
            {
                _logger.Debug($"[RemoteDesktop] EnumDisplayMonitors successfully enumerated {monitorDataList.Count} monitor(s)");
            }

            // Also try EnumDisplayDevices to get additional monitor information and verify/correct bounds
            // This helps ensure we have accurate monitor information
            try
            {
                DISPLAY_DEVICE displayDevice = new DISPLAY_DEVICE();
                displayDevice.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                
                // First, enumerate all display adapters
                for (uint adapterIndex = 0; adapterIndex < 10; adapterIndex++)
                {
                    DISPLAY_DEVICE adapter = new DISPLAY_DEVICE();
                    adapter.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    
                    if (EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
                    {
                        string adapterName = adapter.DeviceName?.TrimEnd('\0') ?? "";
                        
                        // Now enumerate monitors on this adapter
                        for (uint monitorIndex = 0; monitorIndex < 10; monitorIndex++)
                        {
                            DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                            monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                            
                            if (EnumDisplayDevices(adapterName, monitorIndex, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME))
                            {
                                string monitorName = monitor.DeviceName?.TrimEnd('\0') ?? "";
                                string monitorString = monitor.DeviceString?.TrimEnd('\0') ?? "";
                                
                                if (!string.IsNullOrEmpty(monitorName) && (monitor.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                                {
                                    // Try to get actual resolution for this monitor
                                    IntPtr hdc = IntPtr.Zero;
                                    int width = 0;
                                    int height = 0;
                                    
                                    // Try adapter name first
                                    if (!string.IsNullOrEmpty(adapterName))
                                    {
                                        hdc = CreateDC(null, adapterName, null, IntPtr.Zero);
                                        if (hdc != IntPtr.Zero)
                                        {
                                            width = GetDeviceCaps(hdc, HORZRES);
                                            height = GetDeviceCaps(hdc, VERTRES);
                                            DeleteDC(hdc);
                                        }
                                    }
                                    
                                    // If adapter didn't work, try monitor name directly
                                    if ((width == 0 || height == 0) && !string.IsNullOrEmpty(monitorName))
                                    {
                                        hdc = CreateDC(null, monitorName, null, IntPtr.Zero);
                                        if (hdc != IntPtr.Zero)
                                        {
                                            width = GetDeviceCaps(hdc, HORZRES);
                                            height = GetDeviceCaps(hdc, VERTRES);
                                            DeleteDC(hdc);
                                        }
                                    }
                                    
                                    if (width > 0 && height > 0)
                                    {
                                        // Check if we already have this monitor by matching device names or dimensions
                                        // Match by device name OR by same resolution (different monitors can have same resolution)
                                        // Only consider it a duplicate if device name matches exactly
                                        bool exists = monitorDataList.Any(m => 
                                        {
                                            int mWidth = m.Bounds.Right - m.Bounds.Left;
                                            int mHeight = m.Bounds.Bottom - m.Bounds.Top;
                                            
                                            // Match by device name (exact match, case-insensitive) - most reliable
                                            bool nameMatch = !string.IsNullOrEmpty(m.DeviceName) && 
                                                           !string.IsNullOrEmpty(monitorName) &&
                                                           (m.DeviceName.Equals(monitorName, StringComparison.OrdinalIgnoreCase) || 
                                                            (!string.IsNullOrEmpty(monitorString) && m.DeviceName.Equals(monitorString, StringComparison.OrdinalIgnoreCase)));
                                            
                                            // OR match by exact same resolution AND adapter (less reliable but catches some cases)
                                            // Note: We don't match by position here because monitors from different enumeration methods
                                            // may have different position info initially
                                            bool resolutionMatch = Math.Abs(mWidth - width) < 5 && 
                                                                   Math.Abs(mHeight - height) < 5 &&
                                                                   !string.IsNullOrEmpty(m.DeviceName) &&
                                                                   m.DeviceName.Contains(adapterName, StringComparison.OrdinalIgnoreCase);
                                            
                                            return nameMatch || resolutionMatch;
                                        });
                                        
                                        if (!exists)
                                        {
                                            string displayName = !string.IsNullOrEmpty(monitorString) ? monitorString : 
                                                                 !string.IsNullOrEmpty(monitorName) ? monitorName : 
                                                                 adapterName;
                                            
                                            // Try to find matching monitor from EnumDisplayMonitors to get correct position
                                            var matchingMonitor = monitorDataList.FirstOrDefault(m => 
                                            {
                                                int mWidth = m.Bounds.Right - m.Bounds.Left;
                                                int mHeight = m.Bounds.Bottom - m.Bounds.Top;
                                                return Math.Abs(mWidth - width) < 10 && Math.Abs(mHeight - height) < 10;
                                            });
                                            
                                            RECT bounds;
                                            if (matchingMonitor != null)
                                            {
                                                // Use bounds from EnumDisplayMonitors (has correct position)
                                                bounds = matchingMonitor.Bounds;
                                                // Update name if we have a better one
                                                if (!string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(matchingMonitor.DeviceName))
                                                {
                                                    matchingMonitor.DeviceName = displayName;
                                                }
                                            }
                                            else
                                            {
                                                // New monitor - use position from adapter index (approximate)
                                                // For now, position sequentially
                                                int maxX = monitorDataList.Count > 0 ? monitorDataList.Max(m => m.Bounds.Right) : 0;
                                                bounds = new RECT 
                                                { 
                                                    Left = maxX, 
                                                    Top = 0, 
                                                    Right = maxX + width, 
                                                    Bottom = height 
                                                };
                                                
                                                monitorDataList.Add(new MonitorEnumData
                                                {
                                                    DeviceName = displayName,
                                                    Bounds = bounds,
                                                    IsPrimary = (monitor.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0
                                                });
                                                _logger.Info($"[RemoteDesktop] Added new monitor via EnumDisplayDevices: {displayName} ({width}x{height}) at ({bounds.Left},{bounds.Top}), Primary={(monitor.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0}");
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                break; // No more monitors on this adapter
                            }
                        }
                    }
                    else
                    {
                        break; // No more adapters
                    }
                }
            }
            catch (Exception enumDevEx)
            {
                _logger.Debug($"[RemoteDesktop] EnumDisplayDevices failed (non-critical): {enumDevEx.Message}");
            }

            // Remove duplicates based on device name and dimensions
            // Be more lenient to avoid false duplicates - only remove if EXACT match on position AND size
            var uniqueMonitors = new List<MonitorEnumData>();
            foreach (var monitor in monitorDataList)
            {
                int width = monitor.Bounds.Right - monitor.Bounds.Left;
                int height = monitor.Bounds.Bottom - monitor.Bounds.Top;
                
                bool isDuplicate = uniqueMonitors.Any(existing => 
                {
                    int existingWidth = existing.Bounds.Right - existing.Bounds.Left;
                    int existingHeight = existing.Bounds.Bottom - existing.Bounds.Top;
                    
                    // Only consider duplicate if:
                    // 1. Same device name (case-insensitive) AND not empty, OR
                    // 2. EXACT same position AND EXACT same dimensions (within 1 pixel tolerance)
                    bool sameName = !string.IsNullOrEmpty(existing.DeviceName) && 
                                   !string.IsNullOrEmpty(monitor.DeviceName) &&
                                   existing.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase);
                    bool samePositionAndSize = Math.Abs(existing.Bounds.Left - monitor.Bounds.Left) < 2 &&
                                              Math.Abs(existing.Bounds.Top - monitor.Bounds.Top) < 2 &&
                                              Math.Abs(existingWidth - width) < 2 && 
                                              Math.Abs(existingHeight - height) < 2;
                    
                    return sameName || samePositionAndSize;
                });
                
                if (!isDuplicate)
                {
                    uniqueMonitors.Add(monitor);
                }
                else
                {
                    // Update existing monitor with better name if available
                    var existing = uniqueMonitors.FirstOrDefault(e => 
                    {
                        int eWidth = e.Bounds.Right - e.Bounds.Left;
                        int eHeight = e.Bounds.Bottom - e.Bounds.Top;
                        bool sameName = !string.IsNullOrEmpty(e.DeviceName) && 
                                       !string.IsNullOrEmpty(monitor.DeviceName) &&
                                       e.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase);
                        bool samePos = Math.Abs(e.Bounds.Left - monitor.Bounds.Left) < 2 &&
                                      Math.Abs(e.Bounds.Top - monitor.Bounds.Top) < 2 &&
                                      Math.Abs(eWidth - width) < 2 && 
                                      Math.Abs(eHeight - height) < 2;
                        return sameName || samePos;
                    });
                    if (existing != null)
                    {
                        var index = uniqueMonitors.IndexOf(existing);
                        // Update with better name if current is generic
                        if (!string.IsNullOrEmpty(monitor.DeviceName) && 
                            (string.IsNullOrEmpty(uniqueMonitors[index].DeviceName) ||
                             uniqueMonitors[index].DeviceName.StartsWith("Monitor_") ||
                             uniqueMonitors[index].DeviceName.StartsWith("DISPLAY") ||
                             monitor.DeviceName.Length > uniqueMonitors[index].DeviceName.Length))
                        {
                            uniqueMonitors[index] = new MonitorEnumData
                            {
                                DeviceName = monitor.DeviceName,
                                Bounds = uniqueMonitors[index].Bounds,
                                IsPrimary = uniqueMonitors[index].IsPrimary || monitor.IsPrimary
                            };
                        }
                    }
                }
            }

            // Sort by position (primary first, then by X coordinate)
            uniqueMonitors = uniqueMonitors.OrderBy(m => m.IsPrimary ? 0 : 1)
                                          .ThenBy(m => m.Bounds.Left)
                                          .ThenBy(m => m.Bounds.Top)
                                          .ToList();

            for (int i = 0; i < uniqueMonitors.Count; i++)
            {
                var monitorData = uniqueMonitors[i];
                int width = monitorData.Bounds.Right - monitorData.Bounds.Left;
                int height = monitorData.Bounds.Bottom - monitorData.Bounds.Top;
                
                monitors.Add(new MonitorInfo
                {
                    Id = $"DISPLAY{i + 1}",
                    Name = !string.IsNullOrEmpty(monitorData.DeviceName) ? monitorData.DeviceName : $"Display {i + 1}",
                    Width = width,
                    Height = height,
                    X = monitorData.Bounds.Left,
                    Y = monitorData.Bounds.Top,
                    IsPrimary = monitorData.IsPrimary
                });
                
                _logger.Debug($"[RemoteDesktop] Monitor {i + 1}: {monitors[i].Name} ({width}x{height}) at ({monitorData.Bounds.Left},{monitorData.Bounds.Top}), Primary={monitorData.IsPrimary}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Error enumerating monitors using Win32 API: {ex.Message}");
        }

        return monitors;
    }

    private class MonitorEnumData
    {
        public string DeviceName { get; set; } = "";
        public RECT Bounds { get; set; }
        public bool IsPrimary { get; set; }
    }

    public void SelectMonitor(string monitorId)
    {
        if (string.IsNullOrEmpty(monitorId))
        {
            _logger.Warn("SelectMonitor called with null or empty monitor ID");
            return;
        }

        // Validate monitor exists
        var monitors = GetMonitors();
        var selectedMonitor = monitors.FirstOrDefault(m => m.Id == monitorId);
        if (selectedMonitor == null)
        {
            _logger.Warn($"SelectMonitor called with invalid monitor ID: {monitorId}");
            return;
        }

        _currentMonitorId = monitorId;
        // Update input dispatcher with the selected monitor for accurate absolute positioning
        _inputDispatcher.SetCurrentMonitor(monitorId);
        _logger.Info($"[RemoteDesktop] Selected monitor: {monitorId}");

        // CURSOR WARPING: Immediately after selecting a monitor, inject a synthetic absolute mouse move
        // to center the cursor on that specific monitor. This ensures subsequent relative moves from
        // the touchpad happen on the active screen, not a hidden one.
        try
        {
            SystemStateSnapshot? systemState = null;
            if (_systemStateMonitor != null)
            {
                systemState = _systemStateMonitor.GetCurrent();
            }

            // Calculate center of the selected monitor in virtual desktop coordinates
            var centerX = selectedMonitor.X + (selectedMonitor.Width / 2);
            var centerY = selectedMonitor.Y + (selectedMonitor.Height / 2);

            // Warp cursor to center of selected monitor using absolute positioning
            if (_desktopContextSwitcher != null)
            {
                _desktopContextSwitcher.ExecuteInActiveDesktopContext(() =>
                {
                    var warpEvent = new PointerEvent
                    {
                        Kind = PointerEventKind.MoveAbsolute,
                        AbsoluteX = centerX,
                        AbsoluteY = centerY
                    };
                    _inputDispatcher.DispatchPointer(warpEvent);
                }, systemState);
            }
            else
            {
                var warpEvent = new PointerEvent
                {
                    Kind = PointerEventKind.MoveAbsolute,
                    AbsoluteX = centerX,
                    AbsoluteY = centerY
                };
                _inputDispatcher.DispatchPointer(warpEvent);
            }

            _logger.Debug($"[RemoteDesktop] Cursor warped to center of monitor {monitorId} at ({centerX}, {centerY})");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[RemoteDesktop] Failed to warp cursor to monitor {monitorId}: {ex.Message}");
            // Don't fail monitor selection if cursor warping fails
        }
    }

    public string GetCurrentMonitorId()
    {
        return _currentMonitorId;
    }

    public void RegisterFrameSubscriber(IFrameSubscriber subscriber)
    {
        lock (_subscribersLock)
        {
            if (!_subscribers.Contains(subscriber))
            {
                _subscribers.Add(subscriber);
            }
        }
    }

    public void UnregisterFrameSubscriber(IFrameSubscriber subscriber)
    {
        lock (_subscribersLock)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public void InjectPointer(PointerEvent evt)
    {
        if (evt == null)
        {
            _logger.Warn("InjectPointer called with null event");
            return;
        }

        try
        {
            _inputDispatcher.DispatchPointer(evt);
        }
        catch (Exception ex)
        {
            _logger.Error("Error injecting pointer event", ex);
        }
    }

    public void InjectKey(KeyboardEvent evt)
    {
        if (evt == null)
        {
            _logger.Warn("InjectKey called with null event");
            return;
        }

        try
        {
            _inputDispatcher.DispatchKeyboard(evt);
        }
        catch (Exception ex)
        {
            _logger.Error("Error injecting keyboard event", ex);
        }
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        var config = _configManager.GetConfig();
        var targetFrameTime = TimeSpan.FromMilliseconds(1000.0 / config.TargetFps);

        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            var frameStart = DateTimeOffset.UtcNow;

            try
            {
                var frame = GenerateFrame(cancellationToken);
                if (frame != null)
                {
                    // Update last frame timestamp with synchronization
                    lock (_statusLock)
                    {
                        _lastFrameAt = DateTimeOffset.UtcNow;
                        _isDegraded = false; // Reset degraded state on successful capture
                    }
                    _sequenceNumber++;

                    NotifySubscribers(frame);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("[RemoteDesktop] Error in capture loop", ex);
            }

            // Check cancellation before sleeping
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var elapsed = DateTimeOffset.UtcNow - frameStart;
            var sleepTime = targetFrameTime - elapsed;
            if (sleepTime > TimeSpan.Zero)
            {
                // Use cancellation-aware sleep
                try
                {
                    cancellationToken.WaitHandle.WaitOne(sleepTime);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.Info("[RemoteDesktop] Capture loop exited");
    }

    private RemoteFrame? GenerateFrame(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Try real GDI capture first
        var frame = CaptureScreenGdi(cancellationToken);
        if (frame != null)
        {
            // Reset failure count on successful capture (protected by lock)
            lock (_statusLock)
            {
                _captureFailureCount = 0;
            }
            return frame;
        }

        // If capture fails, increment failure count (protected by lock)
        int currentFailureCount;
        lock (_statusLock)
        {
            _captureFailureCount++;
            currentFailureCount = _captureFailureCount;
            
            if (_captureFailureCount >= MaxCaptureFailures)
            {
                _isDegraded = true;
                _captureFailureCount = 0; // Reset after entering degraded state
            }
        }
        
        if (currentFailureCount >= MaxCaptureFailures)
        {
            _logger.Warn($"[RemoteDesktop] Screen capture failed {currentFailureCount} times, entering degraded state");
        }

        // Fallback to synthetic frame if capture fails
        return GenerateFallbackFrame();
    }

    private RemoteFrame? CaptureScreenGdi(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || _captureContext == null)
        {
            return null;
        }

        try
        {
            // Get current system state to determine which session to capture from
            SystemStateSnapshot? systemState = null;
            if (_systemStateMonitor != null)
            {
                systemState = _systemStateMonitor.GetCurrent();
                // Only log session changes, not every frame
                // Removed per-frame logging to reduce event log spam
            }

            // Get the selected monitor's bounds
            var monitors = GetMonitors();
            var selectedMonitor = monitors.FirstOrDefault(m => m.Id == _currentMonitorId);
            
            if (selectedMonitor == null)
            {
                // Fallback to primary monitor if selected monitor not found
                selectedMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
                if (selectedMonitor == null)
                {
                    _logger.Warn("[RemoteDesktop] No monitors available for capture");
                    return null;
                }
            }

            var screenWidth = selectedMonitor.Width;
            var screenHeight = selectedMonitor.Height;

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return null;
            }

            // Ensure capture context has resources allocated
            if (!_captureContext.EnsureResources(screenWidth, screenHeight))
            {
                _logger.Error($"[RemoteDesktop] Failed to allocate GDI resources for monitor {selectedMonitor.Id} ({screenWidth}x{screenHeight})");
                return null;
            }

            // Calculate source coordinates for the selected monitor
            // Use monitor's X/Y coordinates directly from enumeration (virtual desktop coordinates)
            var srcX = selectedMonitor.X;
            var srcY = selectedMonitor.Y;
            
            // Log monitor selection for debugging
            _logger.Debug($"[RemoteDesktop] Capturing monitor {selectedMonitor.Id} ({selectedMonitor.Name}) at ({srcX},{srcY}) size {screenWidth}x{screenHeight}");

            // Capture frame using cross-session capture context
            // The CrossSessionCaptureContext uses multiple methods to capture from any session:
            // - PrintWindow on desktop window (works across sessions as LocalSystem)
            // - Desktop switching with BitBlt
            // - Direct BitBlt fallback
            // Ensure we're in the active desktop context before capturing
            Bitmap? bitmap = null;
            if (_desktopContextSwitcher != null)
            {
                bitmap = _desktopContextSwitcher.ExecuteInActiveDesktopContext(
                    () => _captureContext.CaptureFrame(srcX, srcY, screenWidth, screenHeight, systemState),
                    systemState);
            }
            else
            {
                bitmap = _captureContext.CaptureFrame(srcX, srcY, screenWidth, screenHeight, systemState);
            }

            if (bitmap == null)
            {
                return null;
            }

            using (bitmap)
            {
                // Encode to JPEG
                var jpegBytes = _captureContext.EncodeToJpeg(bitmap);
                if (jpegBytes == null)
                {
                    return null;
                }

                return new RemoteFrame
                {
                    Width = screenWidth,
                    Height = screenHeight,
                    Format = FramePixelFormat.Jpeg,
                    Payload = new ReadOnlyMemory<byte>(jpegBytes),
                    SequenceNumber = _sequenceNumber,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[RemoteDesktop] Error in GDI screen capture: {ex.Message}", ex);
            return null;
        }
    }


    private RemoteFrame? GenerateFallbackFrame()
    {
        // Generate a simple colored bitmap as fallback
        const int width = 1920;
        const int height = 1080;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);

        // Use capture context for encoding if available
        if (_captureContext != null)
        {
            var jpegBytes = _captureContext.EncodeToJpeg(bitmap);
            if (jpegBytes != null)
            {
                return new RemoteFrame
                {
                    Width = width,
                    Height = height,
                    Format = FramePixelFormat.Jpeg,
                    Payload = new ReadOnlyMemory<byte>(jpegBytes),
                    SequenceNumber = _sequenceNumber,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
        }

        return null;
    }

    // P/Invoke declarations for system metrics
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    // Win32 API for monitor enumeration
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    private const int HORZRES = 8;
    private const int VERTRES = 10;

    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    private const int MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT WorkArea;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        
        public MONITORINFOEX()
        {
            Size = 0;
            Monitor = new RECT();
            WorkArea = new RECT();
            Flags = 0;
            DeviceName = string.Empty;
        }
    }

    private void NotifySubscribers(RemoteFrame frame)
    {
        List<IFrameSubscriber> subscribers;
        lock (_subscribersLock)
        {
            subscribers = new List<IFrameSubscriber>(_subscribers);
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber.OnFrame(frame);
            }
            catch (Exception ex)
            {
                _logger.Error("Error notifying frame subscriber", ex);
            }
        }
    }

    private void OnSystemStateChanged(object? sender, SystemStateSnapshot snapshot)
    {
        UpdateStateFromSystemState(snapshot);
    }

    private void UpdateStateFromSystemState(SystemStateSnapshot snapshot)
    {
        var newState = snapshot.DesktopState switch
        {
            DesktopState.LoginScreen => "login_screen",
            DesktopState.Desktop => "desktop",
            DesktopState.Locked => "locked",
            _ => "unknown"
        };

        // Update state with synchronization to prevent data races
        lock (_statusLock)
        {
            if (_currentState != newState)
            {
                _currentState = newState;
                _logger.Info($"[RemoteDesktop] State updated to: {newState} (Session: {snapshot.ActiveSessionId})");
            }
        }
    }
}

