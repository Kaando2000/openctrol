# Openctrol Architecture

This document provides a comprehensive overview of how the Openctrol system works, from high-level architecture to implementation details.

## Table of Contents

1. [System Overview](#system-overview)
2. [Component Architecture](#component-architecture)
3. [Session 0 Isolation & Token Impersonation](#session-0-isolation--token-impersonation)
4. [Screen Capture](#screen-capture)
5. [Input Injection](#input-injection)
6. [Communication Protocols](#communication-protocols)
7. [Security Model](#security-model)
8. [Audio Control](#audio-control)
9. [Power Management](#power-management)
10. [System State Monitoring](#system-state-monitoring)

---

## System Overview

Openctrol is a Windows service that provides remote desktop control, audio management, and power control for Home Assistant. It consists of three main components:

1. **Agent** (C# Windows Service) - Runs on the Windows machine, provides APIs and WebSocket streaming
2. **Home Assistant Integration** (Python) - Custom component that connects to the agent
3. **Frontend Card** (JavaScript) - Custom Lovelace card for Home Assistant UI

```
┌─────────────────┐         ┌──────────────────┐         ┌─────────────────┐
│  Home Assistant │◄───────►│  Openctrol Agent │◄───────►│  Windows Desktop │
│   (Python/JS)    │  HTTP   │   (C# Service)   │  GDI    │   (Session 1+)   │
└─────────────────┘  WebSocket └──────────────────┘  SendInput └─────────────────┘
```

### Key Capabilities

- **Remote Desktop**: Real-time screen capture and streaming (works on login screen, locked desktop, and normal desktop)
- **Input Injection**: Mouse and keyboard input injection with multi-monitor support
- **Audio Control**: Master volume, per-app volume, and device routing
- **Power Management**: Remote restart and shutdown
- **Session 0 Escape**: Works even when Windows is at the login screen or locked

---

## Component Architecture

### Agent Structure

The agent is organized into clear modules:

```
Agent/
├── src/
│   ├── Program.cs                    # Entry point, service host setup
│   ├── Hosting/                      # Service lifecycle management
│   ├── Config/                       # Configuration management
│   ├── Logging/                      # Multi-target logging (Event Log, File, Console)
│   ├── RemoteDesktop/                # Screen capture and streaming
│   │   ├── RemoteDesktopEngine.cs    # Main capture loop
│   │   ├── DesktopContextSwitcher.cs # Session 0 escape via token impersonation
│   │   ├── CaptureContext.cs         # GDI resource management
│   │   └── CrossSessionCaptureContext.cs # Cross-session capture support
│   ├── Input/                        # Input injection
│   │   └── InputDispatcher.cs       # SendInput wrapper
│   ├── Web/                          # HTTP/WebSocket server
│   │   ├── ControlApiServer.cs       # Kestrel HTTP server
│   │   ├── DesktopWebSocketHandler.cs # WebSocket frame streaming
│   │   └── SessionBroker.cs         # Session management
│   ├── Security/                     # Authentication and authorization
│   ├── SystemState/                  # Desktop state detection
│   ├── Audio/                        # Audio device and session control
│   └── Power/                        # System power control
└── tests/                            # Unit tests
```

### Home Assistant Integration Structure

```
HomeAssistant/
├── custom_components/
│   └── openctrol/
│       ├── __init__.py               # Integration setup, service handlers
│       ├── config_flow.py            # Configuration UI
│       ├── api.py                    # REST API client
│       ├── ws.py                     # WebSocket client
│       ├── sensor.py                 # Status sensor entity
│       └── services.yaml             # Service definitions
└── www/
    └── openctrol/
        └── openctrol-card.js         # Frontend card (LitElement)
```

---

## Session 0 Isolation & Token Impersonation

### The Problem

Windows Services run in **Session 0** (isolated session) by default. This means:
- Services cannot see the user's desktop (Session 1+)
- Services cannot interact with the login screen
- Services cannot access user-specific resources
- GDI capture APIs return a "ghost" monitor (WinDisc) instead of real monitors

### The Solution: Token Impersonation

The `DesktopContextSwitcher` class implements **token impersonation** to escape Session 0:

#### Step 1: Get Active Console Session
```csharp
uint sessionId = WTSGetActiveConsoleSessionId();
```
Gets the session ID of the user currently logged in at the console (Session 1+).

#### Step 2: Query User Token
```csharp
WTSQueryUserToken(sessionId, out IntPtr hToken)
```
Retrieves the user's security token for that session.

#### Step 3: Duplicate Token
```csharp
DuplicateTokenEx(
    hToken,
    TOKEN_QUERY | TOKEN_IMPERSONATE | TOKEN_DUPLICATE,
    ref tokenAttributes,
    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
    TOKEN_TYPE.TokenImpersonation,
    out hImpersonationToken)
```
Creates an impersonation token that can be used to impersonate the user.

#### Step 4: Set Thread Token
```csharp
SetThreadToken(IntPtr.Zero, hImpersonationToken)
```
Sets the current thread's token to impersonate the user. Now all API calls on this thread run with the user's security context.

#### Step 5: Access Desktop
```csharp
OpenInputDesktop(...)  // Now works! Can access user's desktop
EnumDisplayMonitors(...)  // Now sees real monitors!
```

### Implementation Flow

```
Service (Session 0, LocalSystem)
    ↓
DesktopContextSwitcher.ImpersonateActiveUser()
    ↓
WTSGetActiveConsoleSessionId() → Session 1
    ↓
WTSQueryUserToken() → User Token
    ↓
DuplicateTokenEx() → Impersonation Token
    ↓
SetThreadToken() → Thread now impersonates user
    ↓
OpenInputDesktop() → Success! Can access user desktop
    ↓
EnumDisplayMonitors() → Success! Sees real monitors
```

### Cleanup

When done, the impersonation is reverted:
```csharp
SetThreadToken(IntPtr.Zero, IntPtr.Zero)  // Remove token
CloseHandle(hImpersonationToken)           // Close handle
```

This is done in `Dispose()` to prevent handle leaks.

---

## Screen Capture

### Capture Method: GDI (Graphics Device Interface)

The agent uses GDI-based screen capture because:
- ✅ Works on login screen (Winlogon desktop)
- ✅ Works on locked desktop
- ✅ Works on normal desktop
- ✅ No special drivers required
- ✅ Cross-session support with token impersonation

### Capture Process

1. **Desktop Context Switch**: Before capture, switch to the active desktop using `DesktopContextSwitcher`
2. **Monitor Selection**: Select the monitor to capture (default: primary monitor)
3. **GDI Capture**:
   ```csharp
   // Get device context for the monitor
   IntPtr hdcScreen = CreateDC("DISPLAY", monitorName, null, IntPtr.Zero);
   
   // Create compatible bitmap
   IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
   
   // Copy screen to bitmap
   BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, SRCCOPY);
   
   // Convert to .NET Bitmap
   Bitmap bitmap = Image.FromHbitmap(hBitmap);
   ```
4. **JPEG Encoding**: Encode bitmap to JPEG using `System.Drawing.Imaging.ImageCodecInfo`
5. **Frame Distribution**: Send to all WebSocket subscribers

### Capture Loop

The `RemoteDesktopEngine` runs a dedicated capture thread:

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    try
    {
        // Switch to active desktop context
        _desktopContextSwitcher.ExecuteInActiveDesktopContext(() =>
        {
            // Capture frame
            var frame = _captureContext.CaptureFrame(selectedMonitor);
            
            // Encode to JPEG
            var jpegBytes = EncodeToJpeg(frame);
            
            // Create RemoteFrame
            var remoteFrame = new RemoteFrame
            {
                Width = frame.Width,
                Height = frame.Height,
                Format = FramePixelFormat.Jpeg,
                Payload = jpegBytes,
                SequenceNumber = Interlocked.Increment(ref _sequenceNumber),
                Timestamp = DateTimeOffset.UtcNow
            };
            
            // Notify subscribers
            NotifySubscribers(remoteFrame);
        }, systemState);
    }
    catch (Exception ex)
    {
        // Handle errors, enter degraded state if repeated failures
    }
    
    // Sleep to maintain target FPS
    await Task.Delay(frameInterval, cancellationToken);
}
```

### Resource Management

The `CaptureContext` class manages GDI resources to prevent leaks:
- Reuses `HDC` (device context) handles
- Reuses `HBITMAP` handles
- Properly disposes all resources in `Dispose()`

### Multi-Monitor Support

- Enumerates monitors using `EnumDisplayMonitors` and `System.Windows.Forms.Screen.AllScreens`
- Combines results from both APIs for maximum compatibility
- Allows selection of specific monitor via WebSocket message
- Supports absolute mouse positioning across monitors

---

## Input Injection

### Input Method: SendInput API

The agent uses Windows `SendInput` API for input injection:
- ✅ Low-level, works at kernel level
- ✅ Works on login screen and locked desktop
- ✅ Supports both relative and absolute mouse positioning
- ✅ Supports keyboard events with modifiers

### Pointer Events

#### Relative Movement
```csharp
INPUT input = new INPUT
{
    type = INPUT_TYPE.MOUSE,
    u = new InputUnion
    {
        mi = new MOUSEINPUT
        {
            dx = deltaX,
            dy = deltaY,
            dwFlags = MOUSEEVENTF.MOVE,
        }
    }
};
SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
```

#### Absolute Movement
Absolute coordinates are normalized to 0-65535 range (Windows requirement):
```csharp
// Map normalized coordinates (0-65535) to monitor bounds
var pixelX = (normalizedX / 65535.0) * monitorWidth;
var pixelY = (normalizedY / 65535.0) * monitorHeight;

// Convert to virtual desktop coordinates
var virtualX = monitorBounds.X + pixelX;
var virtualY = monitorBounds.Y + pixelY;

// Scale to 0-65535 for SendInput
var finalX = ((virtualX - minX) / virtualWidth) * 65535;
var finalY = ((virtualY - minY) / virtualHeight) * 65535;

INPUT input = new INPUT
{
    type = INPUT_TYPE.MOUSE,
    u = new InputUnion
    {
        mi = new MOUSEINPUT
        {
            dx = finalX,
            dy = finalY,
            dwFlags = MOUSEEVENTF.MOVE | MOUSEEVENTF.ABSOLUTE,
        }
    }
};
```

#### Button Events
```csharp
MOUSEEVENTF flags = button switch
{
    MouseButton.Left => MOUSEEVENTF.LEFTDOWN,
    MouseButton.Right => MOUSEEVENTF.RIGHTDOWN,
    MouseButton.Middle => MOUSEEVENTF.MIDDLEDOWN,
    _ => 0
};
```

### Keyboard Events

#### Key Down/Up
```csharp
INPUT input = new INPUT
{
    type = INPUT_TYPE.KEYBOARD,
    u = new InputUnion
    {
        ki = new KEYBDINPUT
        {
            wVk = (ushort)keyCode,
            dwFlags = isKeyDown ? 0 : KEYEVENTF.KEYUP,
        }
    }
};
```

#### Text Input
For text input, uses `VkKeyScanEx` to map characters to virtual key codes with current keyboard layout:
```csharp
short vkScan = VkKeyScanEx(character, keyboardLayout);
int vk = vkScan & 0xFF;
int shiftState = (vkScan >> 8) & 0xFF;

// Send modifiers if needed
if ((shiftState & 1) != 0) SendKey(VK_SHIFT, true);
if ((shiftState & 2) != 0) SendKey(VK_CONTROL, true);
// ... etc

// Send the key
SendKey(vk, true);
SendKey(vk, false);

// Release modifiers
// ...
```

### Desktop Context for Input

All input operations are wrapped with `DesktopContextSwitcher` to ensure they target the correct desktop:
```csharp
_desktopContextSwitcher.ExecuteInActiveDesktopContext(() =>
{
    _inputDispatcher.DispatchPointer(pointerEvent);
}, systemState);
```

---

## Communication Protocols

### REST API

The agent exposes a REST API on port 44325 (configurable):

#### Health Check
```
GET /api/v1/health
```
Returns agent status, uptime, desktop state, active sessions.

#### Create Desktop Session
```
POST /api/v1/sessions/desktop
Body: { "ha_id": "home-assistant", "ttl_seconds": 900 }
Response: { "session_id": "...", "websocket_url": "ws://...", "expires_at": "..." }
```

#### End Session
```
POST /api/v1/sessions/desktop/{sessionId}/end
```

#### Power Control
```
POST /api/v1/power
Body: { "action": "restart" | "shutdown" }
```

#### Audio Control
```
GET /api/v1/audio/state
POST /api/v1/audio/device
POST /api/v1/audio/session
```

### WebSocket Protocol

#### Connection
```
ws://<agent-ip>:44325/api/v1/rd/session?sess=<sessionId>&token=<token>
```

#### Authentication
- Session ID and token are validated via `ISecurityManager`
- Invalid tokens result in immediate connection close

#### Hello Message
On successful connection, agent sends:
```json
{
  "type": "hello",
  "agent_id": "guid",
  "session_id": "session-id",
  "version": "1.0",
  "monitors": [
    {
      "id": "DISPLAY1",
      "name": "Primary",
      "width": 1920,
      "height": 1080,
      "is_primary": true
    }
  ]
}
```

#### Frame Messages (Binary)
Binary WebSocket messages with format:
```
[4 bytes: "OFRA" magic]
[4 bytes: width (int)]
[4 bytes: height (int)]
[4 bytes: format (1 = JPEG)]
[remaining: JPEG bytes]
```

#### Input Messages (JSON)
```json
// Mouse move (relative)
{ "type": "pointer_move", "dx": 10, "dy": 5 }

// Mouse move (absolute, normalized 0-65535)
{ "type": "pointer_move", "absolute": true, "x": 32768, "y": 32768 }

// Mouse click
{ "type": "pointer_click", "button": "left" }

// Mouse button down/up
{ "type": "pointer_button", "button": "right", "action": "down" }

// Keyboard
{ "type": "key", "key_code": 65, "down": true }

// Text input
{ "type": "text", "text": "Hello" }

// Monitor selection
{ "type": "monitor_select", "monitor_id": "DISPLAY2" }
```

---

## Security Model

### API Key Authentication

REST endpoints (except `/api/v1/health`) can require an API key:
- Configured in `AgentConfig.ApiKey`
- Sent via header: `X-Openctrol-Key: <key>` or `Authorization: Bearer <key>`
- Uses constant-time comparison to prevent timing attacks
- If not configured, authentication is disabled (development mode)

### Session Tokens

Desktop sessions require a token:
- Issued by `ISecurityManager.IssueDesktopSessionToken()`
- Contains: token string, HA ID, expiration time
- Validated on WebSocket connection
- Tokens expire after TTL (default: 900 seconds)
- Background timer cleans up expired tokens

### Home Assistant ID Allowlist

- `AgentConfig.AllowedHaIds` contains list of allowed Home Assistant instance IDs
- Empty list = deny-all (secure by default)
- Only Home Assistant instances with IDs in the allowlist can create sessions
- Validated when creating desktop session

### Rate Limiting

- Token validation failures are rate-limited: 5 failures per minute per client IP
- Prevents brute-force attacks on session tokens

### HTTPS Support

- Optional HTTPS using PFX certificate
- Certificate password encrypted with Windows DPAPI (LocalMachine scope)
- Falls back to HTTP if certificate load fails (with logging)

---

## Audio Control

### Architecture

Uses **NAudio** library which wraps Windows Core Audio APIs:
- `MMDeviceEnumerator` - Enumerate audio devices
- `IAudioSessionManager2` - Control per-app audio sessions
- `IPolicyConfig` (COM) - Set default output device

### Device Management

```csharp
// Enumerate devices
var deviceEnumerator = new MMDeviceEnumerator();
var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

// Get device info
foreach (var device in devices)
{
    var deviceInfo = new AudioDeviceInfo
    {
        Id = device.ID,
        Name = device.FriendlyName,
        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
        Muted = device.AudioEndpointVolume.Mute,
        IsDefault = device.ID == defaultDeviceId
    };
}
```

### Per-App Audio Control

```csharp
// Get session manager
var sessionManager = device.AudioSessionManager2;

// Enumerate sessions
var sessions = sessionManager.Sessions;

// Control session volume
var sessionControl = session.QueryInterface<ISimpleAudioVolume>();
sessionControl.MasterVolume = volume;  // 0.0 to 1.0
sessionControl.Mute = muted;
```

### Default Device Selection

Uses COM interface `IPolicyConfig`:
```csharp
var policyConfig = (IPolicyConfig)new PolicyConfigClient();
policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
```

---

## Power Management

### Implementation

Uses Windows API `InitiateSystemShutdownEx`:
```csharp
[DllImport("advapi32.dll")]
static extern bool InitiateSystemShutdownEx(
    string lpMachineName,
    string lpMessage,
    uint dwTimeout,
    bool bForceAppsClosed,
    bool bRebootAfterShutdown,
    uint dwReason
);
```

### Privileges

Requires `SeShutdownPrivilege`:
- Service runs as LocalSystem, which has this privilege by default
- For other accounts, privilege must be explicitly enabled

### Operations

- **Restart**: `InitiateSystemShutdownEx(..., bRebootAfterShutdown: true)`
- **Shutdown**: `InitiateSystemShutdownEx(..., bRebootAfterShutdown: false)`

---

## System State Monitoring

### Purpose

Detects the current desktop state to:
- Switch to correct desktop (Winlogon vs user desktop)
- Report state in health endpoint
- React to state changes (login, logout, lock, unlock)

### Implementation

`SystemStateMonitor` uses:
- `WTSGetActiveConsoleSessionId()` - Get active session
- `OpenInputDesktop()` - Detect if Winlogon desktop is active
- `GetThreadDesktop()` - Detect current desktop

### States

```csharp
public enum DesktopState
{
    Unknown,      // Cannot determine
    LoginScreen,  // Winlogon desktop (login screen)
    Desktop,      // User desktop (logged in)
    Locked        // Desktop is locked
}
```

### State Detection Logic

1. Get active console session ID
2. Try to open Winlogon desktop
   - If successful → LoginScreen or Locked
   - Check if user session exists → Desktop
3. Monitor for changes and raise `StateChanged` event

---

## Data Flow Examples

### Screen Capture Flow

```
1. Home Assistant requests session
POST /api/v1/sessions/desktop
   ↓
2. Agent creates session, returns WebSocket URL
   ↓
3. Home Assistant connects WebSocket
   ws://agent:44325/api/v1/rd/session?sess=...&token=...
   ↓
4. Agent validates token, sends hello message
   ↓
5. Agent subscribes WebSocket to RemoteDesktopEngine
   ↓
6. Capture thread runs:
   - Switch desktop context (token impersonation)
   - Capture frame (GDI BitBlt)
   - Encode to JPEG
   - Send binary message to WebSocket
   ↓
7. Home Assistant receives frame, displays in card
```

### Input Injection Flow

```
1. User interacts with card (mouse move, click, keyboard)
   ↓
2. Card sends JSON message via WebSocket
   { "type": "pointer_move", "dx": 10, "dy": 5 }
   ↓
3. Agent receives message in DesktopWebSocketHandler
   ↓
4. Converts to PointerEvent
   ↓
5. Calls RemoteDesktopEngine.InjectPointer()
   ↓
6. RemoteDesktopEngine calls InputDispatcher.DispatchPointer()
   ↓
7. InputDispatcher switches desktop context
   ↓
8. InputDispatcher calls SendInput() with mouse event
   ↓
9. Windows injects input into active desktop
```

---

## Error Handling & Reliability

### Capture Failures

- Tracks consecutive capture failures
- After 5 failures, enters "degraded" state
- Degraded state reported in health endpoint
- Continues attempting capture (may recover)

### WebSocket Disconnections

- Automatically unsubscribes from frames
- Ends session on disconnect
- Cleans up resources

### Service Recovery

- Windows Service recovery configured:
  - Restart on failure
  - Maximum restart attempts
  - Failure action delay

### Resource Management

- All GDI handles properly disposed
- Token handles closed after use
- Timers disposed on shutdown
- WebSocket connections properly closed

---

## Performance Considerations

### Frame Rate

- Configurable target FPS (default: 30)
- Actual FPS may be lower if encoding is slow
- Drops frames if behind schedule

### JPEG Quality

- Configurable quality (default: 75)
- Higher quality = larger frames = more bandwidth
- Lower quality = smaller frames = less bandwidth

### Multi-Monitor

- Only captures selected monitor (not all monitors)
- Reduces CPU and bandwidth usage
- Client can switch monitors on demand

### Threading

- Capture runs on dedicated thread
- WebSocket handling on ASP.NET Core thread pool
- Input injection on WebSocket thread (blocking, but fast)

---

## Future Enhancements

Potential improvements:
- Hardware-accelerated encoding (H.264 via GPU)
- Multiple concurrent sessions
- Audio streaming (not just control)
- Discovery via mDNS
- Remote file access
- Clipboard synchronization

---

## Conclusion

Openctrol provides a robust, secure, and efficient solution for remote desktop control on Windows. The architecture is designed to:
- Work reliably across all Windows desktop states
- Handle errors gracefully
- Provide secure access control
- Minimize resource usage
- Support multi-monitor setups

The token impersonation mechanism is the key innovation that allows the service to escape Session 0 isolation and interact with the user's desktop, even at the login screen.
