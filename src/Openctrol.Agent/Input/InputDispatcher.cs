using System.Runtime.InteropServices;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Input;

public sealed class InputDispatcher
{
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private string _currentMonitorId = "DISPLAY1"; // Track current monitor for absolute positioning

    public InputDispatcher(ILogger logger)
    {
        _logger = logger;
    }

    public void SetCurrentMonitor(string monitorId)
    {
        lock (_lock)
        {
            _currentMonitorId = monitorId;
        }
    }

    public void DispatchPointer(PointerEvent evt)
    {
        lock (_lock)
        {
            try
            {
                switch (evt.Kind)
                {
                    case PointerEventKind.MoveRelative:
                        MoveMouseRelative(evt.Dx, evt.Dy);
                        break;

                    case PointerEventKind.MoveAbsolute:
                        if (evt.AbsoluteX.HasValue && evt.AbsoluteY.HasValue)
                        {
                            SetCursorPosition(evt.AbsoluteX.Value, evt.AbsoluteY.Value);
                        }
                        break;

                    case PointerEventKind.Button:
                        if (evt.Button.HasValue && evt.ButtonAction.HasValue)
                        {
                            SendMouseButton(evt.Button.Value, evt.ButtonAction.Value == MouseButtonAction.Down);
                        }
                        break;

                    case PointerEventKind.Wheel:
                        SendMouseWheel(evt.WheelDeltaX, evt.WheelDeltaY);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Input] Error dispatching pointer event: {ex.Message}", ex);
            }
        }
    }

    public void DispatchKeyboard(KeyboardEvent evt)
    {
        lock (_lock)
        {
            KeyModifiers modifiersToRelease = KeyModifiers.None;
            try
            {
                switch (evt.Kind)
                {
                    case KeyboardEventKind.KeyDown:
                        if (evt.KeyCode.HasValue)
                        {
                            modifiersToRelease = evt.Modifiers; // Track modifiers to release on error
                            SendKeyDown(evt.KeyCode.Value, evt.Modifiers);
                        }
                        break;

                    case KeyboardEventKind.KeyUp:
                        if (evt.KeyCode.HasValue)
                        {
                            SendKeyUp(evt.KeyCode.Value, evt.Modifiers);
                        }
                        break;

                    case KeyboardEventKind.Text:
                        if (!string.IsNullOrEmpty(evt.Text))
                        {
                            modifiersToRelease = evt.Modifiers; // Track modifiers to release on error
                            SendText(evt.Text, evt.Modifiers);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Input] Error dispatching keyboard event: {ex.Message}", ex);
                // Release any pressed modifiers to prevent stuck keys
                if (modifiersToRelease != KeyModifiers.None)
                {
                    try
                    {
                        ReleaseModifiers(modifiersToRelease);
                    }
                    catch (Exception releaseEx)
                    {
                        _logger.Error($"[Input] Error releasing modifiers after keyboard event failure: {releaseEx.Message}", releaseEx);
                    }
                }
            }
        }
    }

    private void ReleaseModifiers(KeyModifiers modifiers)
    {
        var inputs = new List<INPUT>();

        // Release modifier keys in reverse order of how they're pressed in SendKeyDown
        // Press order: Ctrl → Alt → Shift → Win
        // Release order: Win → Shift → Alt → Ctrl
        if ((modifiers & KeyModifiers.Win) != 0)
        {
            inputs.Add(CreateKeyInput(VK_LWIN, false));
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            inputs.Add(CreateKeyInput(VK_SHIFT, false));
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            inputs.Add(CreateKeyInput(VK_MENU, false));
        }
        if ((modifiers & KeyModifiers.Ctrl) != 0)
        {
            inputs.Add(CreateKeyInput(VK_CONTROL, false));
        }

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    private void MoveMouseRelative(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_TYPE.MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF.MOVE,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SetCursorPosition(int x, int y)
    {
        // Use SendInput with MOUSEEVENTF.ABSOLUTE for proper multi-monitor and DPI support
        // Coordinates from client are relative to the monitor being streamed
        // Use the currently selected monitor's bounds to convert to virtual desktop coordinates
        
        try
        {
            // Get virtual desktop bounds (all monitors combined)
            var virtualDesktop = System.Windows.Forms.Screen.AllScreens;
            if (virtualDesktop.Length == 0)
            {
                _logger.Warn("[Input] No screens available for absolute mouse positioning");
                return;
            }

            // Get the currently selected monitor
            System.Windows.Forms.Screen selectedScreen;
            lock (_lock)
            {
                // Find the screen matching the current monitor ID
                // Monitor IDs are typically like "DISPLAY1", "DISPLAY2", etc.
                // Extract display number from ID (e.g., "DISPLAY1" -> 1, "DISPLAY2" -> 2)
                var displayNumber = 1;
                if (_currentMonitorId.StartsWith("DISPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    var numberPart = _currentMonitorId.Substring(7); // Skip "DISPLAY"
                    if (int.TryParse(numberPart, out var num))
                    {
                        displayNumber = num;
                    }
                }
                
                // Match by index (DISPLAY1 = index 0, DISPLAY2 = index 1, etc.)
                if (displayNumber >= 1 && displayNumber <= virtualDesktop.Length)
                {
                    selectedScreen = virtualDesktop[displayNumber - 1];
                }
                else
                {
                    // Fallback to primary or first screen
                    selectedScreen = System.Windows.Forms.Screen.PrimaryScreen ?? virtualDesktop[0];
                }
            }

            // Client coordinates are relative to the streamed monitor
            // Convert to virtual desktop coordinates using the selected monitor's bounds
            var virtualX = selectedScreen.Bounds.X + x;
            var virtualY = selectedScreen.Bounds.Y + y;

            // Get virtual desktop dimensions
            var minX = virtualDesktop.Min(s => s.Bounds.Left);
            var minY = virtualDesktop.Min(s => s.Bounds.Top);
            var maxX = virtualDesktop.Max(s => s.Bounds.Right);
            var maxY = virtualDesktop.Max(s => s.Bounds.Bottom);
            var virtualWidth = maxX - minX;
            var virtualHeight = maxY - minY;

            if (virtualWidth <= 0 || virtualHeight <= 0)
            {
                _logger.Warn("[Input] Invalid virtual desktop dimensions");
                return;
            }

            // Scale to 0-65535 range as required by SendInput with MOUSEEVENTF.ABSOLUTE
            // Normalize coordinates relative to virtual desktop
            var normalizedX = (int)(((double)(virtualX - minX) / virtualWidth) * 65535);
            var normalizedY = (int)(((double)(virtualY - minY) / virtualHeight) * 65535);

            // Clamp to valid range
            normalizedX = Math.Max(0, Math.Min(65535, normalizedX));
            normalizedY = Math.Max(0, Math.Min(65535, normalizedY));

            var input = new INPUT
            {
                type = INPUT_TYPE.MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = normalizedX,
                        dy = normalizedY,
                        dwFlags = MOUSEEVENTF.MOVE | MOUSEEVENTF.ABSOLUTE,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error in absolute mouse positioning: {ex.Message}", ex);
        }
    }

    private void SendMouseButton(MouseButton button, bool down)
    {
        MOUSEEVENTF flags = 0;
        switch (button)
        {
            case MouseButton.Left:
                flags = down ? MOUSEEVENTF.LEFTDOWN : MOUSEEVENTF.LEFTUP;
                break;
            case MouseButton.Right:
                flags = down ? MOUSEEVENTF.RIGHTDOWN : MOUSEEVENTF.RIGHTUP;
                break;
            case MouseButton.Middle:
                flags = down ? MOUSEEVENTF.MIDDLEDOWN : MOUSEEVENTF.MIDDLEUP;
                break;
        }

        var input = new INPUT
        {
            type = INPUT_TYPE.MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SendMouseWheel(int deltaX, int deltaY)
    {
        if (deltaY != 0)
        {
            var input = new INPUT
            {
                type = INPUT_TYPE.MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        mouseData = deltaY,
                        dwFlags = MOUSEEVENTF.WHEEL,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        if (deltaX != 0)
        {
            var input = new INPUT
            {
                type = INPUT_TYPE.MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        mouseData = deltaX,
                        dwFlags = MOUSEEVENTF.HWHEEL,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
    }

    private void SendKeyDown(int keyCode, KeyModifiers modifiers)
    {
        var inputs = new List<INPUT>();

        // Send modifier keys first
        if ((modifiers & KeyModifiers.Ctrl) != 0)
        {
            inputs.Add(CreateKeyInput(VK_CONTROL, true));
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            inputs.Add(CreateKeyInput(VK_MENU, true));
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            inputs.Add(CreateKeyInput(VK_SHIFT, true));
        }
        if ((modifiers & KeyModifiers.Win) != 0)
        {
            inputs.Add(CreateKeyInput(VK_LWIN, true));
        }

        // Send the main key
        inputs.Add(CreateKeyInput(keyCode, true));

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    private void SendKeyUp(int keyCode, KeyModifiers modifiers)
    {
        var inputs = new List<INPUT>();

        // Send the main key first
        inputs.Add(CreateKeyInput(keyCode, false));

        // Then release modifier keys
        if ((modifiers & KeyModifiers.Ctrl) != 0)
        {
            inputs.Add(CreateKeyInput(VK_CONTROL, false));
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            inputs.Add(CreateKeyInput(VK_MENU, false));
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            inputs.Add(CreateKeyInput(VK_SHIFT, false));
        }
        if ((modifiers & KeyModifiers.Win) != 0)
        {
            inputs.Add(CreateKeyInput(VK_LWIN, false));
        }

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    private void SendText(string text, KeyModifiers modifiers)
    {
        foreach (var ch in text)
        {
            var scanCode = VkKeyScanEx(ch, GetKeyboardLayout(0));
            if (scanCode == -1)
            {
                continue;
            }

            // Cast to ushort first to avoid sign extension issues
            var vk = (ushort)scanCode & 0xFF;
            var shiftFromScan = ((ushort)scanCode & 0x100) != 0;
            
            // Determine if shift is needed: use from modifiers if provided, otherwise from VkKeyScanEx
            var needsShift = (modifiers & KeyModifiers.Shift) != 0 || shiftFromScan;

            var inputs = new List<INPUT>();

            // Apply modifier keys first (Ctrl, Alt, Win from parameter)
            if ((modifiers & KeyModifiers.Ctrl) != 0)
            {
                inputs.Add(CreateKeyInput(VK_CONTROL, true));
            }
            if ((modifiers & KeyModifiers.Alt) != 0)
            {
                inputs.Add(CreateKeyInput(VK_MENU, true));
            }
            if ((modifiers & KeyModifiers.Win) != 0)
            {
                inputs.Add(CreateKeyInput(VK_LWIN, true));
            }
            // Apply shift if needed (either from modifiers or from VkKeyScanEx, but only once)
            if (needsShift)
            {
                inputs.Add(CreateKeyInput(VK_SHIFT, true));
            }

            // Send the main key
            inputs.Add(CreateKeyInput(vk, true));
            inputs.Add(CreateKeyInput(vk, false));

            // Release modifier keys in reverse order
            if (needsShift)
            {
                inputs.Add(CreateKeyInput(VK_SHIFT, false));
            }
            if ((modifiers & KeyModifiers.Win) != 0)
            {
                inputs.Add(CreateKeyInput(VK_LWIN, false));
            }
            if ((modifiers & KeyModifiers.Alt) != 0)
            {
                inputs.Add(CreateKeyInput(VK_MENU, false));
            }
            if ((modifiers & KeyModifiers.Ctrl) != 0)
            {
                inputs.Add(CreateKeyInput(VK_CONTROL, false));
            }

            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            }
        }
    }

    private INPUT CreateKeyInput(int vk, bool down)
    {
        return new INPUT
        {
            type = INPUT_TYPE.KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = down ? 0 : KEYEVENTF.KEYUP,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    // P/Invoke declarations
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);


    [DllImport("user32.dll")]
    private static extern short VkKeyScanEx(char ch, IntPtr hkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    // Constants
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public INPUT_TYPE type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public MOUSEEVENTF dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KEYEVENTF dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private enum INPUT_TYPE : uint
    {
        MOUSE = 0,
        KEYBOARD = 1,
        HARDWARE = 2
    }

    [Flags]
    private enum MOUSEEVENTF : uint
    {
        MOVE = 0x0001,
        LEFTDOWN = 0x0002,
        LEFTUP = 0x0004,
        RIGHTDOWN = 0x0008,
        RIGHTUP = 0x0010,
        MIDDLEDOWN = 0x0020,
        MIDDLEUP = 0x0040,
        WHEEL = 0x0800,
        HWHEEL = 0x1000,
        ABSOLUTE = 0x8000
    }

    [Flags]
    private enum KEYEVENTF : uint
    {
        KEYUP = 0x0002,
        UNICODE = 0x0004,
        SCANCODE = 0x0008
    }
}

