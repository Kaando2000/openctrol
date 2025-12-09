using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Openctrol.Agent.SystemState;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.RemoteDesktop;

/// <summary>
/// Cross-session capture context that can capture from any session (user sessions, login screen, etc.)
/// Uses multiple capture methods with fallbacks for maximum reliability.
/// </summary>
internal sealed class CrossSessionCaptureContext : IDisposable
{
    private readonly ILogger _logger;
    private readonly ImageCodecInfo? _jpegEncoder;
    private readonly EncoderParameters _encoderParameters;
    
    // GDI resources for fallback capture
    private IntPtr _hdcScreen = IntPtr.Zero;
    private IntPtr _hdcMem = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private int _currentWidth;
    private int _currentHeight;
    
    private bool _disposed;

    public CrossSessionCaptureContext(ILogger logger)
    {
        _logger = logger;
        
        // Get JPEG encoder once
        _jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
        
        // Create encoder parameters once (quality 75)
        _encoderParameters = new EncoderParameters(1);
        _encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 75L);
    }

    public bool EnsureResources(int width, int height)
    {
        if (_disposed)
        {
            return false;
        }

        // If dimensions changed or resources not allocated, recreate
        if (_hdcScreen == IntPtr.Zero || width != _currentWidth || height != _currentHeight)
        {
            ReleaseResources();

            _hdcScreen = GetDC(IntPtr.Zero);
            if (_hdcScreen == IntPtr.Zero)
            {
                return false;
            }

            _hdcMem = CreateCompatibleDC(_hdcScreen);
            if (_hdcMem == IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, _hdcScreen);
                _hdcScreen = IntPtr.Zero;
                return false;
            }

            _hBitmap = CreateCompatibleBitmap(_hdcScreen, width, height);
            if (_hBitmap == IntPtr.Zero)
            {
                DeleteDC(_hdcMem);
                ReleaseDC(IntPtr.Zero, _hdcScreen);
                _hdcMem = IntPtr.Zero;
                _hdcScreen = IntPtr.Zero;
                return false;
            }

            _oldBitmap = SelectObject(_hdcMem, _hBitmap);
            _currentWidth = width;
            _currentHeight = height;
        }

        return true;
    }

    public Bitmap? CaptureFrame(int srcX, int srcY, int width, int height, SystemStateSnapshot? systemState = null)
    {
        if (_disposed || _hdcMem == IntPtr.Zero || _hBitmap == IntPtr.Zero)
        {
            return null;
        }

        // Try multiple capture methods in order of preference
        // Method 1: Try PrintWindow on desktop window (works across sessions when running as LocalSystem)
        var bitmap = TryCaptureWithPrintWindow(srcX, srcY, width, height, systemState);
        if (bitmap != null)
        {
            return bitmap;
        }

        // Method 2: Try desktop switching with BitBlt
        bitmap = TryCaptureWithDesktopSwitch(srcX, srcY, width, height, systemState);
        if (bitmap != null)
        {
            return bitmap;
        }

        // Method 3: Fallback to direct BitBlt (may work for login screen)
        return TryCaptureDirect(srcX, srcY, width, height);
    }

    private Bitmap? TryCaptureWithPrintWindow(int srcX, int srcY, int width, int height, SystemStateSnapshot? systemState)
    {
        try
        {
            // Get the desktop window - this should work from Session 0 when running as LocalSystem
            IntPtr hDesktopWnd = GetDesktopWindow();
            if (hDesktopWnd == IntPtr.Zero)
            {
                return null;
            }

            // Try to get the active console session's desktop window
            IntPtr hTargetDesktop = IntPtr.Zero;
            IntPtr hOriginalDesktop = GetThreadDesktop(GetCurrentThreadId());
            
            try
            {
                // Determine which desktop to use - prioritize user session when available
                bool useUserSession = false;
                if (systemState != null)
                {
                    // Use user session if we have an active session and it's not the system session
                    if (systemState.ActiveSessionId > 0 && 
                        systemState.ActiveSessionId != unchecked((int)0xFFFFFFFF) &&
                        (systemState.DesktopState == DesktopState.Desktop || 
                         systemState.DesktopState == DesktopState.LoginScreen))
                    {
                        useUserSession = true;
                    }
                }
                else
                {
                    // If no system state available, try user session by default
                    useUserSession = true;
                }

                // Try to open the input desktop first (most reliable for active user session)
                hTargetDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                if (hTargetDesktop == IntPtr.Zero)
                {
                    // Try user session desktop
                    if (useUserSession)
                    {
                        hTargetDesktop = OpenDesktop("Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                        if (hTargetDesktop == IntPtr.Zero)
                        {
                            // Try WinSta0\Default (user session desktop)
                            hTargetDesktop = OpenDesktop("WinSta0\\Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                        }
                    }
                    // Fallback to Winlogon for login screen
                    if (hTargetDesktop == IntPtr.Zero)
                    {
                        hTargetDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                    }
                }

                if (hTargetDesktop != IntPtr.Zero && SetThreadDesktop(hTargetDesktop))
                {
                    hDesktopWnd = GetDesktopWindow();
                }
            }
            catch
            {
                // Continue with original desktop
            }

            // Use PrintWindow to capture the desktop window
            // This can work across sessions when running as LocalSystem
            // Get DC from desktop window for BitBlt (better for multi-monitor)
            IntPtr hdcDesktop = IntPtr.Zero;
            
            if (hTargetDesktop != IntPtr.Zero)
            {
                // We've switched to the target desktop, get DC from desktop window
                hdcDesktop = GetDC(hDesktopWnd);
            }
            
            if (hdcDesktop == IntPtr.Zero)
            {
                // Fallback: get DC directly from desktop window
                hdcDesktop = GetDC(hDesktopWnd);
            }
            
            bool captureSuccess = false;
            
            if (hdcDesktop != IntPtr.Zero)
            {
                try
                {
                    // Use BitBlt to capture the specific monitor region directly
                    // This works better for multi-monitor setups
                    captureSuccess = BitBlt(_hdcMem, 0, 0, width, height, hdcDesktop, srcX, srcY, SRCCOPY);
                }
                finally
                {
                    ReleaseDC(hDesktopWnd, hdcDesktop);
                }
            }
            
            if (hTargetDesktop != IntPtr.Zero)
            {
                try
                {
                    SetThreadDesktop(hOriginalDesktop);
                    CloseDesktopHandle(hTargetDesktop);
                }
                catch { }
            }

            if (captureSuccess)
            {
                return Image.FromHbitmap(_hBitmap);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"[CrossSessionCapture] PrintWindow method failed: {ex.Message}");
        }

        return null;
    }

    private Bitmap? TryCaptureWithDesktopSwitch(int srcX, int srcY, int width, int height, SystemStateSnapshot? systemState)
    {
        IntPtr hOriginalDesktop = IntPtr.Zero;
        IntPtr hInputDesktop = IntPtr.Zero;
        bool desktopSwitched = false;
        IntPtr hdcSource = _hdcScreen;

        try
        {
            hOriginalDesktop = GetThreadDesktop(GetCurrentThreadId());
            
            bool captureFromUserSession = false;
            if (systemState != null)
            {
                // Use user session if we have an active session (including login screen)
                if (systemState.ActiveSessionId > 0 && 
                    systemState.ActiveSessionId != unchecked((int)0xFFFFFFFF) &&
                    (systemState.DesktopState == DesktopState.Desktop || 
                     systemState.DesktopState == DesktopState.LoginScreen))
                {
                    captureFromUserSession = true;
                }
            }
            else
            {
                // If no system state available, try user session by default
                captureFromUserSession = true;
            }
            
            // Try to open the input desktop first (most reliable for active user session)
            hInputDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
            
            if (hInputDesktop == IntPtr.Zero && captureFromUserSession)
            {
                // Try user session desktop
                hInputDesktop = OpenDesktop("Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                if (hInputDesktop == IntPtr.Zero)
                {
                    // Try WinSta0\Default (user session desktop)
                    hInputDesktop = OpenDesktop("WinSta0\\Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                }
            }
            
            if (hInputDesktop == IntPtr.Zero && !captureFromUserSession)
            {
                hInputDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
            }
            
            if (hInputDesktop != IntPtr.Zero)
            {
                if (SetThreadDesktop(hInputDesktop))
                {
                    desktopSwitched = true;
                    hdcSource = GetDC(IntPtr.Zero);
                    if (hdcSource == IntPtr.Zero)
                    {
                        SetThreadDesktop(hOriginalDesktop);
                        desktopSwitched = false;
                        CloseDesktopHandle(hInputDesktop);
                        hInputDesktop = IntPtr.Zero;
                        hdcSource = _hdcScreen;
                    }
                }
                else
                {
                    CloseDesktopHandle(hInputDesktop);
                    hInputDesktop = IntPtr.Zero;
                }
            }

            // Try BitBlt capture
            bool bitBltResult = BitBlt(_hdcMem, 0, 0, width, height, hdcSource, srcX, srcY, SRCCOPY);
            
            if (desktopSwitched && hdcSource != IntPtr.Zero && hdcSource != _hdcScreen)
            {
                ReleaseDC(IntPtr.Zero, hdcSource);
            }
            
            if (desktopSwitched && hOriginalDesktop != IntPtr.Zero)
            {
                try
                {
                    SetThreadDesktop(hOriginalDesktop);
                }
                catch { }
            }
            
            if (hInputDesktop != IntPtr.Zero)
            {
                CloseDesktopHandle(hInputDesktop);
            }

            if (bitBltResult)
            {
                return Image.FromHbitmap(_hBitmap);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"[CrossSessionCapture] Desktop switch method failed: {ex.Message}");
            
            if (desktopSwitched && hdcSource != IntPtr.Zero && hdcSource != _hdcScreen)
            {
                ReleaseDC(IntPtr.Zero, hdcSource);
            }
            
            if (desktopSwitched && hOriginalDesktop != IntPtr.Zero)
            {
                try
                {
                    SetThreadDesktop(hOriginalDesktop);
                }
                catch { }
            }
            
            if (hInputDesktop != IntPtr.Zero)
            {
                CloseDesktopHandle(hInputDesktop);
            }
        }

        return null;
    }

    private Bitmap? TryCaptureDirect(int srcX, int srcY, int width, int height)
    {
        try
        {
            bool bitBltResult = BitBlt(_hdcMem, 0, 0, width, height, _hdcScreen, srcX, srcY, SRCCOPY);
            if (bitBltResult)
            {
                return Image.FromHbitmap(_hBitmap);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"[CrossSessionCapture] Direct capture method failed: {ex.Message}");
        }

        return null;
    }

    public byte[]? EncodeToJpeg(Bitmap bitmap)
    {
        if (_disposed || _jpegEncoder == null)
        {
            return null;
        }

        // Estimate buffer size: width * height * 3 bytes per pixel * compression ratio (assume ~0.1 for JPEG)
        // Add 10KB overhead for JPEG headers and safety margin
        // Use long for calculation to prevent integer overflow on very large displays
        long estimatedSizeLong = ((long)bitmap.Width * bitmap.Height * 3 / 10) + 10240;
        // Cap at reasonable maximum (10MB) to avoid excessive allocations
        estimatedSizeLong = Math.Min(estimatedSizeLong, 10 * 1024 * 1024L);
        // Ensure minimum size
        estimatedSizeLong = Math.Max(estimatedSizeLong, 64 * 1024L);
        // Cast to int (safe after clamping)
        int estimatedSize = estimatedSizeLong > int.MaxValue ? int.MaxValue : (int)estimatedSizeLong;

        byte[]? rentedBuffer = null;
        try
        {
            // Rent buffer from ArrayPool to reduce GC pressure
            rentedBuffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
            
            using var ms = new MemoryStream(rentedBuffer, 0, rentedBuffer.Length, true, true);
            bitmap.Save(ms, _jpegEncoder, _encoderParameters);
            
            // Copy only the used portion to a new array (required for return value)
            // This is still better than ToArray() on a growing stream because we avoid
            // multiple reallocations during encoding
            var result = new byte[ms.Position];
            Buffer.BlockCopy(rentedBuffer, 0, result, 0, (int)ms.Position);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            // Return buffer to pool
            if (rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private void ReleaseResources()
    {
        if (_hdcMem != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
        {
            SelectObject(_hdcMem, _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }

        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        if (_hdcMem != IntPtr.Zero)
        {
            DeleteDC(_hdcMem);
            _hdcMem = IntPtr.Zero;
        }

        if (_hdcScreen != IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, _hdcScreen);
            _hdcScreen = IntPtr.Zero;
        }

        _currentWidth = 0;
        _currentHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseResources();
        _encoderParameters?.Dispose();
        _disposed = true;
    }

    // P/Invoke declarations
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
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    private static void CloseDesktopHandle(IntPtr hDesktop)
    {
        CloseDesktop(hDesktop);
    }

    private const int SRCCOPY = 0x00CC0020;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
}
