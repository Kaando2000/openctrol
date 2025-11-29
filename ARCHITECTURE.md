1. Finished application: what 1.0 should do

A single Windows Service (OpenctrolAgent) that:

Starts at boot and runs under a high-privilege service account (or LocalSystem) so it works:

on login screen,

when the desktop is locked,

on normal desktop.

Exposes:

an HTTPS REST API on the LAN (/api/v1/...) for:

health,

creating/ending desktop sessions,

power (restart/shutdown),

audio control,

a WebSocket endpoint (/ws/desktop) that:

streams screen frames (JPEG),

receives mouse + keyboard events.

Implements remote desktop:

Captures the active console desktop (including Winlogon).

Encodes frames to JPEG and sends over WS.

Injects mouse/keyboard events using SendInput.

Supports multi-monitor:

Enumerates monitors.

Streams one selected monitor at a time (configurable via WS messages).

Provides power controls:

Restart / shutdown via Windows APIs.

Provides audio controls:

List output devices, default device.

Adjust master and per-app volumes.

Change default output device.

Announces itself on LAN via mDNS:

Service name _openctrol._tcp.local.

TXT: agent id, version, capabilities.

Is reliable:

Recovers from capture failures.

Handles RDP engine restart internally.

Works across reboots and logon/logoff/lock events.

2. Repo / solution layout (final state)

At repo root (on your E:\Proje\openctrol and GitHub repo):

openctrol/
  src/
    Openctrol.Agent/
      Openctrol.Agent.csproj
      Program.cs
      Hosting/
        AgentHost.cs
      Config/
        AgentConfig.cs
        JsonConfigManager.cs
      Logging/
        ILogger.cs
        EventLogLogger.cs
        FileLogger.cs
        CompositeLogger.cs
      Security/
        SessionToken.cs
        ISecurityManager.cs
        SecurityManager.cs
      SystemState/
        DesktopState.cs
        SystemStateSnapshot.cs
        ISystemStateMonitor.cs
        SystemStateMonitor.cs
      RemoteDesktop/
        RemoteDesktopStatus.cs
        MonitorInfo.cs
        RemoteFrame.cs
        IFrameSubscriber.cs
        IRemoteDesktopEngine.cs
        RemoteDesktopEngine.cs      // GDI-based capture
      Input/
        PointerEvent.cs
        KeyboardEvent.cs
        InputDispatcher.cs
      Web/
        IControlApiServer.cs
        ControlApiServer.cs
        DesktopWebSocketHandler.cs
        Dtos/
          HealthDto.cs
          CreateDesktopSessionDto.cs
          PowerDto.cs
          AudioDtos.cs
      Audio/
        IAudioManager.cs
        AudioManager.cs
      Power/
        IPowerManager.cs
        PowerManager.cs
      Discovery/
        IDiscoveryBroadcaster.cs
        MdnsDiscoveryBroadcaster.cs
  tools/
    install-service.ps1
    uninstall-service.ps1
  docs/
    ARCHITECTURE.md
    API.md
    BUILD.md
  tests/
    Openctrol.Agent.Tests/
      ...
README.md


Everything lives in one service process (Openctrol.Agent) but structured into clear modules.

3. Foundation: hosting, config, logging
3.1. Hosting (Program.cs + Hosting/AgentHost.cs)

Use .NET Generic Host with Windows service integration:

public static class Program
{
    public static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices((ctx, services) =>
            {
                services.AddSingleton<IConfigManager, JsonConfigManager>();
                services.AddSingleton<ILogger, CompositeLogger>();
                services.AddSingleton<ISecurityManager, SecurityManager>();
                services.AddSingleton<ISystemStateMonitor, SystemStateMonitor>();
                services.AddSingleton<IRemoteDesktopEngine, RemoteDesktopEngine>();
                services.AddSingleton<IAudioManager, AudioManager>();
                services.AddSingleton<IPowerManager, PowerManager>();
                services.AddSingleton<IDiscoveryBroadcaster, MdnsDiscoveryBroadcaster>();
                services.AddSingleton<IControlApiServer, ControlApiServer>();

                services.AddHostedService<AgentHost>();
            })
            .Build()
            .Run();
    }
}


AgentHost : BackgroundService:

On start: start RDE, API server, discovery.

On stop: stop discovery, API server, RDE.

3.2. Config (Config/AgentConfig.cs, JsonConfigManager.cs)

AgentConfig (final shape):

public sealed class AgentConfig
{
    public string AgentId { get; set; } = "";
    public int HttpPort { get; set; } = 44325;
    public int MaxSessions { get; set; } = 1;
    public string CertPath { get; set; } = "";
    public string CertPasswordEncrypted { get; set; } = "";
    public int TargetFps { get; set; } = 30;
    public IList<string> AllowedHaIds { get; set; } = new List<string>();
}


JsonConfigManager:

Reads %ProgramData%\Openctrol\config.json.

Creates default if missing:

Random AgentId (GUID).

Default port 44325.

Reload() re-reads file.

3.3. Logging (Logging/ILogger.cs, EventLogLogger.cs, FileLogger.cs, CompositeLogger.cs)

ILogger:

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}


EventLogLogger:

Logs to Windows Event Log source OpenctrolAgent.

FileLogger:

Appends to daily rolling log file in %ProgramData%\Openctrol\logs.

CompositeLogger:

Fans out to both.

4. Shafts & wiring: state, remote desktop, WS, REST
4.1. System state (SystemState/)

Models:

public enum DesktopState { Unknown, LoginScreen, Desktop, Locked }

public sealed class SystemStateSnapshot
{
    public int ActiveSessionId { get; init; }
    public DesktopState DesktopState { get; init; }
}


Interface:

public interface ISystemStateMonitor
{
    SystemStateSnapshot GetCurrent();
    event EventHandler<SystemStateSnapshot>? StateChanged;
}


Implementation SystemStateMonitor:

Uses WTS APIs (WTSGetActiveConsoleSessionId etc.) and desktop APIs:

Detect active session.

Detect Winlogon vs user desktop vs locked.

Periodically updates snapshot and raises StateChanged.

4.2. Remote desktop core (RemoteDesktop/)
Models
public sealed class RemoteDesktopStatus
{
    public bool IsRunning { get; init; }
    public DateTimeOffset LastFrameAt { get; init; }
    public string State { get; init; } = "unknown"; // login_screen / desktop / locked
}

public sealed class MonitorInfo
{
    public string Id { get; init; } = "";  // e.g. "\\.\DISPLAY1"
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
}

public enum FramePixelFormat { Jpeg }

public sealed class RemoteFrame
{
    public int Width { get; init; }
    public int Height { get; init; }
    public FramePixelFormat Format { get; init; }
    public ReadOnlyMemory<byte> Payload { get; init; }  // JPEG bytes
    public long SequenceNumber { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

Interfaces
public interface IFrameSubscriber
{
    void OnFrame(RemoteFrame frame);
}

public interface IRemoteDesktopEngine
{
    void Start();
    void Stop();

    RemoteDesktopStatus GetStatus();
    IReadOnlyList<MonitorInfo> GetMonitors();
    void SelectMonitor(string monitorId);

    void RegisterFrameSubscriber(IFrameSubscriber subscriber);
    void UnregisterFrameSubscriber(IFrameSubscriber subscriber);

    void InjectPointer(PointerEvent evt);
    void InjectKey(KeyboardEvent evt);
}

Implementation: RemoteDesktopEngine

Uses GDI capture for v1 (works on login screen, locked desktop).

Core behaviour:

On Start:

Hook into ISystemStateMonitor.StateChanged.

Start capture thread:

1 loop per frame:

Capture active monitor with BitBlt.

Encode to JPEG.

Update LastFrameAt.

Call OnFrame for all subscribers.

Target config.TargetFps, but drop if slow.

On Stop:

Stop thread and release resources.

GetMonitors:

Use EnumDisplayMonitors / System.Windows.Forms.Screen to list monitors.

SelectMonitor:

Update internal CurrentMonitorId.

InjectPointer / InjectKey:

Delegate to InputDispatcher (see below).

4.3. Input (Input/)
Models
public enum PointerEventKind { MoveRelative, MoveAbsolute, Button, Wheel }
public enum MouseButton { Left, Right, Middle }
public enum MouseButtonAction { Down, Up }

public sealed class PointerEvent
{
    public PointerEventKind Kind { get; init; }
    public int Dx { get; init; }
    public int Dy { get; init; }
    public int? AbsoluteX { get; init; }
    public int? AbsoluteY { get; init; }
    public MouseButton? Button { get; init; }
    public MouseButtonAction? ButtonAction { get; init; }
    public int WheelDeltaX { get; init; }
    public int WheelDeltaY { get; init; }
}

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

public enum KeyboardEventKind { KeyDown, KeyUp, Text }

public sealed class KeyboardEvent
{
    public KeyboardEventKind Kind { get; init; }
    public int? KeyCode { get; init; } // virtual key
    public string? Text { get; init; }
    public KeyModifiers Modifiers { get; init; }
}

Implementation: InputDispatcher

P/Invoke SendInput, SetCursorPos, MapVirtualKey, etc.

Responsibilities:

Convert PointerEvent → mouse moves, clicks, wheel.

Convert KeyboardEvent → series of keyboard SendInput calls.

Handle Text events with layout-aware mapping (via VkKeyScanEx).

RemoteDesktopEngine.InjectPointer/InjectKey simply call into InputDispatcher.

4.4. Security & sessions (Security/)
Models & interface
public sealed class SessionToken
{
    public string Token { get; init; } = "";
    public string HaId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

public interface ISecurityManager
{
    bool IsHaAllowed(string haId);
    SessionToken IssueDesktopSessionToken(string haId, TimeSpan ttl);
    bool TryValidateDesktopSessionToken(string token, out SessionToken validated);
}

Implementation: SecurityManager

Uses in-memory dictionary for active tokens.

Later: integrate with TLS client certs / pinned HA IDs.

4.5. Session broker (can live in Web/ or own folder)
public sealed class DesktopSession
{
    public string SessionId { get; init; } = "";
    public string HaId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public bool IsActive { get; set; }
}

public interface ISessionBroker
{
    DesktopSession StartDesktopSession(string haId, TimeSpan ttl);
    bool TryGetSession(string sessionId, out DesktopSession session);
    void EndSession(string sessionId);
    IReadOnlyList<DesktopSession> GetActiveSessions();
}


Enforces MaxSessions.

Cleans expired sessions with background timer.

4.6. Web: REST + WebSocket (Web/)
4.6.1. Control API server

IControlApiServer:

public interface IControlApiServer
{
    void Start();
    Task StopAsync();
}


ControlApiServer:

Wraps a Kestrel-hosted ASP.NET Core app.

Listens on AgentConfig.HttpPort on LAN.

Sets up:

GET /api/v1/health

POST /api/v1/sessions/desktop

POST /api/v1/sessions/desktop/{id}/end

POST /api/v1/power

GET /api/v1/audio/state

POST /api/v1/audio/device

POST /api/v1/audio/session

/ws/desktop WebSocket

4.6.2. DTOs (Web/Dtos/)

HealthDto:

public sealed class HealthDto
{
    public string AgentId { get; init; } = "";
    public long UptimeSeconds { get; init; }
    public RemoteDesktopHealthDto RemoteDesktop { get; init; } = new();
    public int ActiveSessions { get; init; }
}

public sealed class RemoteDesktopHealthDto
{
    public bool IsRunning { get; init; }
    public DateTimeOffset LastFrameAt { get; init; }
    public string State { get; init; } = "unknown";
}


CreateDesktopSessionRequest/Response:

public sealed class CreateDesktopSessionRequest
{
    public string HaId { get; init; } = "";
    public int TtlSeconds { get; init; } = 900;
}

public sealed class CreateDesktopSessionResponse
{
    public string SessionId { get; init; } = "";
    public string WebSocketUrl { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}


PowerRequest:

public sealed class PowerRequest
{
    public string Action { get; init; } = ""; // "restart" | "shutdown"
}


Audio DTOs for /audio/state, /audio/device, /audio/session.

4.6.3. WebSocket handler (DesktopWebSocketHandler.cs)

Responsibilities:

Handle path /ws/desktop.

Parse query: ?sess=<sessionId>&token=<token>.

Validate token via ISecurityManager.

On success:

Send hello JSON:

{
  "type": "hello",
  "agent_id": "<AgentId>",
  "session_id": "<sess>",
  "version": "1.0",
  "monitors": [
    { "id": "DISPLAY1", "name": "Primary", "width": 1920, "height": 1080, "is_primary": true }
  ]
}


Subscribe as IFrameSubscriber to IRemoteDesktopEngine.

Start a loop:

Receive messages:

parse JSON for:

pointer_move, pointer_button, pointer_wheel,

key, text,

monitor_select,

quality.

Convert to PointerEvent / KeyboardEvent and call engine.

Send frames:

binary WS messages with header + JPEG payload.

4.6.4. Frame binary format

Final decision: simple header + JPEG body:

Binary WS message layout:

4 bytes: ASCII "OFRA" (Openctrol Frame)

4 bytes: int width

4 bytes: int height

4 bytes: int format (1 = JPEG)

remaining: JPEG bytes

So client does:

Read first 16 bytes → parse header.

Remaining bytes → decode JPEG.

5. Rooms: Audio, Power, Discovery
5.1. Power (Power/)

Interface:

public interface IPowerManager
{
    void Restart();
    void Shutdown();
}


Implementation (PowerManager):

P/Invoke InitiateSystemShutdownEx with:

flags for reboot vs shutdown.

Requires SeShutdownPrivilege.

5.2. Audio (Audio/)

Interface:

public interface IAudioManager
{
    AudioState GetState();
    void SetDeviceVolume(string deviceId, float volume, bool muted);
    void SetSessionVolume(string sessionId, float volume, bool muted);
    void SetDefaultOutputDevice(string deviceId);
}


Models:

public sealed class AudioState
{
    public string DefaultOutputDeviceId { get; init; } = "";
    public IReadOnlyList<AudioDeviceInfo> Devices { get; init; } = Array.Empty<AudioDeviceInfo>();
    public IReadOnlyList<AudioSessionInfo> Sessions { get; init; } = Array.Empty<AudioSessionInfo>();
}

public sealed class AudioDeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class AudioSessionInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Volume { get; init; }
    public bool Muted { get; init; }
}


Implementation:

Use NAudio CoreAudio wrappers (MMDevice, IAudioSessionManager2).

5.3. Discovery (Discovery/)

Interface:

public interface IDiscoveryBroadcaster
{
    void Start();
    void Stop();
}


MdnsDiscoveryBroadcaster:

Uses Zeroconf or Makaretu.MDNS.

Advertises _openctrol._tcp.local on HttpPort.

TXT:

id=<AgentId>

ver=<appVersion>

cap=desktop,input

6. Plumbing: security, TLS, reliability
6.1. TLS

Kestrel listens on:

HTTP (dev) / HTTPS (prod) at HttpPort.

Certificate from AgentConfig.CertPath PFX; password decrypted with DPAPI.

Later: enable mTLS and check HA client cert.

6.2. Reliability

The service itself:

installed with SCM recovery: restart on failure.

RemoteDesktopEngine:

internal try/catch around capture.

if capture repeatedly fails, backoff + retry.

WebSocket:

if client disconnects, unsubscribes from frames, ends session.

/api/v1/health exposes:

last frame timestamp,

engine running state,

active sessions.

7. Construction sequence for Cursor (high level)

Treat these as phases; each can be one or more Cursor prompts.

Phase 1 – Foundation

Create solution & Openctrol.Agent project with folder structure.

Implement AgentConfig, JsonConfigManager.

Implement ILogger, EventLogLogger, FileLogger, CompositeLogger.

Implement Program.cs + AgentHost to start/stop components (with stubs).

Implement ControlApiServer with only /api/v1/health (stub data).

Phase 2 – Remote desktop skeleton

Implement SystemStateSnapshot, DesktopState, ISystemStateMonitor, SystemStateMonitor (stub returns desktop).

Implement RemoteDesktopStatus, MonitorInfo, RemoteFrame, IFrameSubscriber, IRemoteDesktopEngine.

Implement RemoteDesktopEngine with synthetic frames (just colored bitmaps) and logging for InjectPointer/InjectKey.

Phase 3 – WebSocket wiring

Implement ISecurityManager, SessionToken, SecurityManager (in-memory tokens).

Implement ISessionBroker, DesktopSession, SessionBroker.

Extend ControlApiServer:

POST /api/v1/sessions/desktop

POST /api/v1/sessions/desktop/{id}/end

Implement DesktopWebSocketHandler:

validate sess + token,

send hello JSON,

subscribe to frames,

parse JSON input messages and log them.

Phase 4 – Real capture & input

Replace synthetic frames in RemoteDesktopEngine with GDI capture of primary monitor; encode JPEG.

Implement PointerEvent, KeyboardEvent, InputDispatcher using SendInput.

Wire RemoteDesktopEngine.InjectPointer/InjectKey to InputDispatcher.

Phase 5 – System state & login screen

Implement real SystemStateMonitor:

Active session detection,

login/desktop/locked.

Make RemoteDesktopEngine attach to correct desktop based on SystemStateSnapshot.

Manual tests:

Desktop streaming + input.

Locked screen.

Fully logged-out login screen.

Phase 6 – Power, audio, discovery

Implement IPowerManager, PowerManager + /api/v1/power.

Implement IAudioManager, AudioManager + /api/v1/audio/* endpoints.

Implement IDiscoveryBroadcaster, MdnsDiscoveryBroadcaster and start/stop it in AgentHost.

Phase 7 – TLS, hardening, scripts

Configure Kestrel for HTTPS using cert from config.

Add basic rate limiting for token validation failures.

Enhance /api/v1/health with full detailed status.

Add install-service.ps1 and uninstall-service.ps1.

Write docs/API.md describing REST and WS protocols.