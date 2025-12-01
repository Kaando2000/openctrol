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
    private CaptureContext? _captureContext;
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
        _captureContext = new CaptureContext();
        _logger.Info("[RemoteDesktop] Starting...");

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
        // Update input dispatcher with the selected monitor for accurate absolute positioning
        _inputDispatcher.SetCurrentMonitor(monitorId);
        _logger.Info($"[RemoteDesktop] Selected monitor: {monitorId}");
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

            // Capture frame using reusable context
            using var bitmap = _captureContext.CaptureFrame(srcX, srcY, screenWidth, screenHeight);
            if (bitmap == null)
            {
                return null;
            }

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

