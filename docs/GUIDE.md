# Openctrol Complete Guide

This guide covers building, installing, and using the Openctrol Agent.

## Table of Contents

1. [Building from Source](#building-from-source)
2. [Installation](#installation)
3. [Configuration](#configuration)
4. [API Reference](#api-reference)
5. [Troubleshooting](#troubleshooting)

---

## Building from Source

### Prerequisites

- .NET 8 SDK or later
- Windows 10/11 or Windows Server 2016+
- Administrator privileges (for service installation)

### Build from Command Line

```powershell
# Build the entire solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Run tests
dotnet test
```

### Build from Visual Studio

1. Open `openctrol.sln` in Visual Studio 2022 or later
2. Select Build > Build Solution (or press Ctrl+Shift+B)
3. For Release build, change the configuration dropdown to "Release"

### Project Structure

```
openctrol/
├── Agent/
│   ├── src/              # Main service project
│   ├── tests/             # Unit tests
│   └── setup/             # Installation scripts
├── HomeAssistant/         # Home Assistant integration
└── docs/                  # Documentation
```

### Output

After building, the executable will be located at:
- Debug: `Agent/src/bin/Debug/net8.0-windows/Openctrol.Agent.exe`
- Release: `Agent/src/bin/Release/net8.0-windows/Openctrol.Agent.exe`

### Publishing for Deployment

To create a deployment-ready package, use the setup script:

```powershell
cd Agent\setup
.\setup.ps1
```

This will:
- Build the solution
- Publish to `Agent/setup/bin` (self-contained)
- Install the service

### Manual Publish Command

```powershell
# Publish self-contained agent (recommended for deployment)
dotnet publish Agent\src\Openctrol.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false

# Output location:
# Agent/src/bin/Release/net8.0-windows/win-x64/publish/
```

### Dependencies

The project uses the following NuGet packages:
- `Microsoft.Extensions.Hosting.WindowsServices` - Windows Service hosting
- `Microsoft.AspNetCore.App` - Web framework (implicit)
- `NAudio` - Audio management
- `System.Drawing.Common` - Image encoding

All dependencies are automatically restored during build.

### Running as Console App (Development)

For development and testing, you can run the service as a console application:

```powershell
cd Agent\src
dotnet run -- --console
```

The service will start and listen on the configured port (default: 44325).

---

## Installation

### Quick Installation

1. **Download and extract the release**
   - Download the latest release ZIP from the [Releases](https://github.com/yourusername/openctrol/releases) page
   - Extract the ZIP file to a folder (e.g., `C:\Openctrol`)

2. **Run the installer**
   - Open PowerShell as Administrator (Right-click → "Run as Administrator")
   - Navigate to the extracted folder
   - Run the setup script:
     ```powershell
     cd Agent\setup
     powershell -ExecutionPolicy Bypass -File .\setup.ps1
     ```

The setup script will:
- Build and publish the agent (if needed)
- Copy binaries to `C:\Program Files\Openctrol`
- Create configuration at `C:\ProgramData\Openctrol\config.json`
- Install and start the Windows service
- Optionally create a firewall rule

### Installation Options

```powershell
# Custom port and API key
.\setup.ps1 -Port 8080 -ApiKey "my-secret-key"

# With HTTPS
.\setup.ps1 -UseHttps -CertPath "C:\certs\cert.pfx" -CertPassword "password"

# Skip firewall rule
.\setup.ps1 -CreateFirewallRule:$false

# Use existing binaries (skip build)
.\setup.ps1 -SkipBuild
```

### Installation Locations

- **Binaries**: `C:\Program Files\Openctrol`
- **Configuration**: `C:\ProgramData\Openctrol\config.json`
- **Logs**: `C:\ProgramData\Openctrol\logs\`
- **Service**: `OpenctrolAgent` (Windows Service)

### Uninstallation

```powershell
# Uninstall (preserves configuration and logs)
cd Agent\setup
.\uninstall.ps1

# Uninstall and remove everything
.\uninstall.ps1 -RemoveProgramData
```

### Verify Installation

- Open a browser and navigate to: `http://localhost:44325/api/v1/health`
- You should see a JSON response with agent status
- Check Windows Services (`services.msc`) for "Openctrol Agent" service

---

## Configuration

Configuration is stored at `C:\ProgramData\Openctrol\config.json`. The installer creates this file with your settings, or you can edit it manually:

```json
{
  "AgentId": "your-unique-agent-id",
  "HttpPort": 44325,
  "MaxSessions": 1,
  "TargetFps": 30,
  "ApiKey": "your-api-key-here",
  "AllowedHaIds": ["home-assistant-1"],
  "CertPath": "",
  "CertPasswordEncrypted": ""
}
```

### Configuration Options

- **AgentId**: Unique identifier for this agent (auto-generated if not set)
- **HttpPort**: HTTP/HTTPS port (default: 44325)
- **MaxSessions**: Maximum concurrent desktop sessions (default: 1)
- **TargetFps**: Target frame rate for screen capture (default: 30)
- **ApiKey**: API key for REST endpoint authentication (empty = no auth)
- **AllowedHaIds**: List of allowed Home Assistant instance IDs (empty = deny-all)
- **CertPath**: Path to PFX certificate file for HTTPS (optional)
- **CertPasswordEncrypted**: Encrypted certificate password (optional)

### Local Web UI

After installation, access the local control panel at:

**`http://localhost:44325/ui`** (or `https://localhost:44325/ui` if HTTPS is enabled)

The UI provides:
- Service status and health monitoring
- Configuration management
- Service controls (start/stop/restart)
- Uninstall option

**Note**: The UI is only accessible from localhost for security.

---

## API Reference

### Base URL

- HTTP: `http://<agent-ip>:44325`
- HTTPS: `https://<agent-ip>:44325` (when certificate is configured)

### Authentication

REST API endpoints (except `/api/v1/health`) require authentication via API key when configured:

- **Header**: `X-Openctrol-Key: <api-key>` OR
- **Header**: `Authorization: Bearer <api-key>`

If no API key is configured in `AgentConfig.ApiKey`, authentication is disabled (backward compatibility / development mode).

### REST Endpoints

#### Health Check

**GET** `/api/v1/health`

Returns agent status, uptime, desktop state, active sessions.

**Response:**
```json
{
  "agent_id": "guid-string",
  "uptime_seconds": 12345,
  "remote_desktop": {
    "is_running": true,
    "last_frame_at": "2024-01-01T12:00:00Z",
    "state": "desktop"
  },
  "active_sessions": 1
}
```

#### Create Desktop Session

**POST** `/api/v1/sessions/desktop`

Creates a new desktop session for remote control.

**Request:**
```json
{
  "ha_id": "home-assistant",
  "ttl_seconds": 900
}
```

**Response:**
```json
{
  "session_id": "session-guid",
  "websocket_url": "ws://agent:44325/api/v1/rd/session?sess=...&token=...",
  "expires_at": "2024-01-01T12:15:00Z"
}
```

#### End Session

**POST** `/api/v1/sessions/desktop/{sessionId}/end`

Ends a desktop session.

#### Power Control

**POST** `/api/v1/power`

Controls system power.

**Request:**
```json
{
  "action": "restart"  // or "shutdown"
}
```

#### Audio State

**GET** `/api/v1/audio/state`

Returns current audio state (devices, sessions, volumes).

**Response:**
```json
{
  "default_output_device_id": "device-guid",
  "devices": [
    {
      "id": "device-guid",
      "name": "Speakers",
      "volume": 0.75,
      "muted": false,
      "is_default": true
    }
  ],
  "sessions": [
    {
      "id": "session-guid",
      "name": "Chrome",
      "volume": 0.5,
      "muted": false,
      "output_device_id": "device-guid"
    }
  ]
}
```

#### Set Device Volume

**POST** `/api/v1/audio/device`

Sets volume and mute state for an audio device.

**Request:**
```json
{
  "device_id": "device-guid",
  "volume": 0.75,
  "muted": false
}
```

#### Set Session Volume

**POST** `/api/v1/audio/session`

Sets volume and mute state for an audio session (per-app).

**Request:**
```json
{
  "session_id": "session-guid",
  "volume": 0.5,
  "muted": false
}
```

#### Set Default Output Device

**POST** `/api/v1/audio/device/default`

Sets the default output device.

**Request:**
```json
{
  "device_id": "device-guid"
}
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

For complete API documentation, see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Troubleshooting

### Build Errors

- **Missing .NET 8 SDK**: Install from https://dotnet.microsoft.com/download
- **NuGet restore issues**: Run `dotnet restore` manually
- **Windows-specific APIs**: Ensure you're building on Windows

### Service Installation Issues

- **"Access Denied"**: Run PowerShell as Administrator
- **Service won't start**: Check Event Log for errors
- **Port already in use**: Change the port in `%ProgramData%\Openctrol\config.json`

### Runtime Errors

- **Port already in use**: Change the port in configuration
- **Certificate errors**: Ensure certificate path and password are correct in config
- **Permission errors**: Run as Administrator or configure appropriate service account
- **Session 0 isolation**: Token impersonation should handle this automatically

### Service Not Responding

1. Check service status: `Get-Service OpenctrolAgent`
2. Check Event Log: `Get-EventLog -LogName Application -Source OpenctrolAgent -Newest 10`
3. Check port listening: `netstat -ano | findstr :44325`
4. Check health endpoint: `Invoke-WebRequest http://localhost:44325/api/v1/health`

### Screen Capture Issues

- **Ghost monitor (WinDisc)**: Token impersonation may have failed. Check logs for Session 0 isolation errors. The agent should automatically impersonate the active user to escape Session 0.
- **No frames**: Check if capture thread is running, verify desktop state
- **Poor performance**: Reduce target FPS or JPEG quality in configuration
- **Monitor enumeration issues**: The agent uses multiple APIs (`EnumDisplayMonitors` and `Screen.AllScreens`) to detect monitors. If only one monitor is detected, check Event Log for desktop context errors.

### Input Injection Issues

- **Inputs not working**: Verify desktop context switching is working. Check logs for "Successfully impersonated active console user".
- **Wrong coordinates**: Check monitor selection and coordinate mapping. Absolute coordinates are normalized to 0-65535 range.
- **Stuck keys**: Service should automatically release modifiers on error

### Audio Control Issues

- **Device not found**: Verify device ID is correct. Use `/api/v1/audio/state` to list available devices.
- **Volume changes not working**: May require admin privileges for some operations. The service runs as LocalSystem which should have sufficient privileges.
- **Per-app routing not working**: Windows API limitation, some apps don't support routing

### WebSocket Connection Issues

- **Connection refused**: Check service is running and port is open
- **Authentication failed**: Verify session token is valid and not expired. Tokens expire after TTL (default: 900 seconds).
- **No frames received**: Check if session is active and capture is running. Verify desktop state is not "unknown".

### Monitor Selection Issues

- **Monitor button not working**: Check browser console for JavaScript errors. Ensure WebSocket connection is active.
- **Wrong monitor selected**: Verify monitor IDs match between agent and client. Monitor IDs are case-sensitive.

### Common Solutions

1. **Restart the service**: `Restart-Service OpenctrolAgent`
2. **Check logs**: `C:\ProgramData\Openctrol\logs\`
3. **Verify configuration**: `C:\ProgramData\Openctrol\config.json`
4. **Reinstall**: Run uninstall script, then install again
5. **Check Event Log**: `Get-EventLog -LogName Application -Source OpenctrolAgent -Newest 20`

### Debugging Tips

- Enable console mode for detailed logging: Run service with `--console` flag
- Check WebSocket messages in browser developer tools (Network tab)
- Verify token impersonation is working: Look for "Successfully impersonated active console user" in logs
- Test health endpoint first: `Invoke-WebRequest http://localhost:44325/api/v1/health`

For more detailed troubleshooting and architecture details, see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Additional Resources

- [Architecture Documentation](ARCHITECTURE.md) - Comprehensive system architecture
- [Security Documentation](../SECURITY.md) - Security model and best practices
- [Contributing Guide](../CONTRIBUTING.md) - How to contribute to the project

