using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Openctrol.Agent.SystemState;

namespace Openctrol.Agent.RemoteDesktop;

/// <summary>
/// Reusable GDI capture context to avoid allocating resources per frame.
/// Allocates HDC, HBITMAP, and encoder resources once and reuses them.
/// </summary>
internal sealed class CaptureContext : IDisposable
{
    private IntPtr _hdcScreen = IntPtr.Zero;
    private IntPtr _hdcMem = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private int _currentWidth;
    private int _currentHeight;
    private readonly ImageCodecInfo? _jpegEncoder;
    private readonly EncoderParameters _encoderParameters;
    private bool _disposed;

    public CaptureContext()
    {
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
            // Release old resources if they exist
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

        try
        {
            // Switch to the active console desktop before capture to handle login/locked screens
            // This ensures we capture from the session that is currently receiving input:
            // - Session 0: Login screen (Winlogon desktop)
            // - Session 1+: User session (Default desktop)
            IntPtr hOriginalDesktop = IntPtr.Zero;
            IntPtr hInputDesktop = IntPtr.Zero;
            bool desktopSwitched = false;

            IntPtr hdcSource = _hdcScreen; // Use class field as default
            
            try
            {
                // Get current thread desktop
                hOriginalDesktop = GetThreadDesktop(GetCurrentThreadId());
                
                // Determine which desktop to capture from based on system state
                // Priority: If user session is active (Session 1+), capture from user session
                // Otherwise, capture from login screen (Session 0)
                
                bool captureFromUserSession = false;
                if (systemState != null)
                {
                    // If we have an active user session (Session 1+) and desktop is active, capture from user session
                    if (systemState.ActiveSessionId > 0 && 
                        systemState.ActiveSessionId != unchecked((int)0xFFFFFFFF) &&
                        systemState.DesktopState == DesktopState.Desktop)
                    {
                        captureFromUserSession = true;
                    }
                }
                
                // Try to open the input desktop first (this is the desktop receiving input)
                // This will automatically be the active desktop (Winlogon for login, Default for user session)
                hInputDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                
                // If OpenInputDesktop failed, try opening Default desktop for user sessions
                if (hInputDesktop == IntPtr.Zero && captureFromUserSession)
                {
                    hInputDesktop = OpenDesktop("Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                }
                
                // If still failed and we need Winlogon (login screen), try opening it directly
                if (hInputDesktop == IntPtr.Zero && !captureFromUserSession)
                {
                    hInputDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
                }
                
                if (hInputDesktop != IntPtr.Zero)
                {
                    // Switch thread to input desktop
                    if (SetThreadDesktop(hInputDesktop))
                    {
                        desktopSwitched = true;
                        // Get a new DC from the switched desktop for capture
                        hdcSource = GetDC(IntPtr.Zero);
                        if (hdcSource == IntPtr.Zero)
                        {
                            // Failed to get DC after switch, restore and continue with original
                            SetThreadDesktop(hOriginalDesktop);
                            desktopSwitched = false;
                            CloseDesktopHandle(hInputDesktop);
                            hInputDesktop = IntPtr.Zero;
                            hdcSource = _hdcScreen; // Fall back to original DC
                        }
                    }
                    else
                    {
                        // Failed to switch, close handle and continue with original desktop
                        CloseDesktopHandle(hInputDesktop);
                        hInputDesktop = IntPtr.Zero;
                    }
                }
            }
            catch
            {
                // If desktop switching fails, continue with current desktop
                if (hInputDesktop != IntPtr.Zero)
                {
                    CloseDesktopHandle(hInputDesktop);
                    hInputDesktop = IntPtr.Zero;
                }
            }

            try
            {
                // IMPORTANT: For multi-monitor setups, srcX/srcY are in virtual desktop coordinates
                // BitBlt with MOUSEEVENTF.ABSOLUTE coordinates requires conversion to normalized coordinates (0-65535)
                // But we're using device coordinates here, so we use srcX/srcY directly
                
                // Capture screen to bitmap using the source DC (from switched desktop if successful)
                // srcX/srcY are already in virtual desktop coordinates from monitor enumeration
                bool bitBltResult = BitBlt(_hdcMem, 0, 0, width, height, hdcSource, srcX, srcY, SRCCOPY);
                
                if (!bitBltResult)
                {
                    // BitBlt failed - might be because we're not in the right session
                    // Log error for debugging
                    int error = Marshal.GetLastWin32Error();
                    // Don't log every failure as it can spam logs, but capture the error for debugging
                    return null;
                }
                
                // Convert to managed Bitmap
                return Image.FromHbitmap(_hBitmap);
            }
            finally
            {
                // Release the DC we acquired for the switched desktop (if different from class field)
                if (desktopSwitched && hdcSource != IntPtr.Zero && hdcSource != _hdcScreen)
                {
                    ReleaseDC(IntPtr.Zero, hdcSource);
                }
                
                // Restore original desktop if we switched
                if (desktopSwitched && hOriginalDesktop != IntPtr.Zero)
                {
                    try
                    {
                        SetThreadDesktop(hOriginalDesktop);
                    }
                    catch
                    {
                        // Ignore errors restoring desktop
                    }
                }
                
                // Clean up input desktop handle
                if (hInputDesktop != IntPtr.Zero)
                {
                    CloseDesktopHandle(hInputDesktop);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    public byte[]? EncodeToJpeg(Bitmap bitmap)
    {
        if (_disposed || _jpegEncoder == null)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, _jpegEncoder, _encoderParameters);
            return ms.ToArray();
        }
        catch
        {
            return null;
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
}

