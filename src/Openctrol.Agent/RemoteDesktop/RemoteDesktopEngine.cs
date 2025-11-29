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
    private bool _isRunning;
    private DateTimeOffset _lastFrameAt;
    private long _sequenceNumber;
    private string _currentState = "unknown";
    private string _currentMonitorId = "DISPLAY1";
    private int _captureFailureCount = 0;
    private const int MaxCaptureFailures = 5;

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
        _logger.Info("RemoteDesktopEngine starting...");

        // Hook into system state monitor to react to state changes
        if (_systemStateMonitor != null)
        {
            _systemStateMonitor.StateChanged += OnSystemStateChanged;
            // Update current state from initial system state
            var initialState = _systemStateMonitor.GetCurrent();
            UpdateStateFromSystemState(initialState);
        }

        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "RemoteDesktopCapture"
        };
        _captureThread.Start();

        _logger.Info("RemoteDesktopEngine started");
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _logger.Info("RemoteDesktopEngine stopping...");

        // Unhook from system state monitor
        if (_systemStateMonitor != null)
        {
            _systemStateMonitor.StateChanged -= OnSystemStateChanged;
        }

        _captureThread?.Join(TimeSpan.FromSeconds(5));
        _captureThread = null;

        _logger.Info("RemoteDesktopEngine stopped");
    }

    public RemoteDesktopStatus GetStatus()
    {
        // Synchronize access to status fields that are written from multiple threads
        lock (_statusLock)
        {
            return new RemoteDesktopStatus
            {
                IsRunning = _isRunning,
                LastFrameAt = _lastFrameAt,
                State = _currentState
            };
        }
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                monitors.Add(new MonitorInfo
                {
                    Id = $"DISPLAY{i + 1}",
                    Name = screen.DeviceName,
                    Width = screen.Bounds.Width,
                    Height = screen.Bounds.Height,
                    IsPrimary = screen.Primary
                });
            }
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
                IsPrimary = true
            });
        }

        return monitors;
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
        if (!monitors.Any(m => m.Id == monitorId))
        {
            _logger.Warn($"SelectMonitor called with invalid monitor ID: {monitorId}");
            return;
        }

        _currentMonitorId = monitorId;
        _logger.Info($"Selected monitor: {monitorId}");
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

    private void CaptureLoop()
    {
        var config = _configManager.GetConfig();
        var targetFrameTime = TimeSpan.FromMilliseconds(1000.0 / config.TargetFps);

        while (_isRunning)
        {
            var frameStart = DateTimeOffset.UtcNow;

            try
            {
                var frame = GenerateSyntheticFrame();
                if (frame != null)
                {
                    // Update last frame timestamp with synchronization
                    lock (_statusLock)
                    {
                        _lastFrameAt = DateTimeOffset.UtcNow;
                    }
                    _sequenceNumber++;

                    NotifySubscribers(frame);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error in capture loop", ex);
            }

            var elapsed = DateTimeOffset.UtcNow - frameStart;
            var sleepTime = targetFrameTime - elapsed;
            if (sleepTime > TimeSpan.Zero)
            {
                Thread.Sleep(sleepTime);
            }
        }
    }

    private RemoteFrame? GenerateSyntheticFrame()
    {
        // Try real GDI capture first
        var frame = CaptureScreenGdi();
        if (frame != null)
        {
            _captureFailureCount = 0;
            return frame;
        }

        // If capture fails, increment failure count
        _captureFailureCount++;
        if (_captureFailureCount >= MaxCaptureFailures)
        {
            _logger.Warn($"Screen capture failed {_captureFailureCount} times, backing off");
            Thread.Sleep(1000); // Backoff
            _captureFailureCount = 0;
        }

        // Fallback to synthetic frame if capture fails
        return GenerateFallbackFrame();
    }

    private RemoteFrame? CaptureScreenGdi()
    {
        try
        {
            // Get the selected monitor's bounds
            var monitors = GetMonitors();
            var selectedMonitor = monitors.FirstOrDefault(m => m.Id == _currentMonitorId);
            
            if (selectedMonitor == null)
            {
                // Fallback to primary monitor if selected monitor not found
                selectedMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
                if (selectedMonitor == null)
                {
                    _logger.Warn("No monitors available for capture");
                    return null;
                }
            }

            var screenWidth = selectedMonitor.Width;
            var screenHeight = selectedMonitor.Height;

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return null;
            }

            // Get the device context for the entire virtual screen
            var hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var hdcMem = CreateCompatibleDC(hdcScreen);
                if (hdcMem == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    var hBitmap = CreateCompatibleBitmap(hdcScreen, screenWidth, screenHeight);
                    if (hBitmap == IntPtr.Zero)
                    {
                        return null;
                    }

                    try
                    {
                        var oldBitmap = SelectObject(hdcMem, hBitmap);
                        
                        // Calculate source coordinates for the selected monitor
                        // For multi-monitor setups, capture from the correct position based on monitor bounds
                        var srcX = 0;
                        var srcY = 0;
                        
                        // If not primary monitor, get its position from screen bounds
                        if (!selectedMonitor.IsPrimary)
                        {
                            var screens = System.Windows.Forms.Screen.AllScreens;
                            var screen = screens.FirstOrDefault(s => s.DeviceName == selectedMonitor.Name);
                            if (screen != null)
                            {
                                srcX = screen.Bounds.X;
                                srcY = screen.Bounds.Y;
                            }
                        }
                        
                        BitBlt(hdcMem, 0, 0, screenWidth, screenHeight, hdcScreen, srcX, srcY, SRCCOPY);
                        SelectObject(hdcMem, oldBitmap);

                        using var bitmap = Image.FromHbitmap(hBitmap);
                        return EncodeToJpeg(bitmap);
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                finally
                {
                    DeleteDC(hdcMem);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error in GDI screen capture", ex);
            return null;
        }
    }

    private RemoteFrame? EncodeToJpeg(Bitmap bitmap)
    {
        try
        {
            using var ms = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

            if (encoder == null)
            {
                return null;
            }

            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 75L);

            bitmap.Save(ms, encoder, parameters);
            var jpegBytes = ms.ToArray();

            return new RemoteFrame
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                Format = FramePixelFormat.Jpeg,
                Payload = new ReadOnlyMemory<byte>(jpegBytes),
                SequenceNumber = _sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Error encoding bitmap to JPEG", ex);
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

        return EncodeToJpeg(bitmap);
    }

    // P/Invoke declarations for GDI capture
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;

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
                _logger.Info($"Remote desktop state updated to: {newState} (Session: {snapshot.ActiveSessionId})");
            }
        }
    }
}

