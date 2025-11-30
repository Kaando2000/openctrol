# Openctrol Agent

A Windows service that provides remote desktop control, audio management, and power control for Home Assistant over your local network.

## Features

- **Remote Desktop Control**
  - Real-time screen capture and streaming (works on login screen, locked desktop, and normal desktop)
  - Mouse and keyboard input injection (including touchpad-only mode)
  - Multi-monitor support with monitor selection

- **Audio Control**
  - Master volume and mute for output devices
  - Per-app volume control
  - Per-app output device routing
  - Default output device selection

- **Power Management**
  - Remote restart and shutdown

- **Security**
  - API key authentication for REST endpoints
  - Home Assistant ID allowlist (deny-all by default)
  - Session-based authentication with token expiration
  - HTTPS support with certificate configuration
  - Local network only (no internet exposure)

## Quick Start

### Installation

**⚠ PREREQUISITE: .NET 8.0 Runtime must be installed before running the MSI.**

1. **Install .NET 8.0 Runtime** (if not already installed)
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Install either "Desktop Runtime" or "ASP.NET Core Runtime"
   - Verify: `dotnet --list-runtimes` should show `Microsoft.NETCore.App 8.0.x`

2. **Download the installer**
   - Download `OpenctrolAgentSetup.msi` from the [Releases](https://github.com/yourusername/openctrol/releases) page or the `dist/` folder

3. **Run the installer**
   - Right-click `OpenctrolAgentSetup.msi` and select "Install"
   - Follow the installation wizard:
     - Configure the HTTP port (default: 44325)
     - Optionally enable HTTPS and provide certificate details
     - Set or generate an API key
     - Optionally create a Windows Firewall rule
   - The service will be installed and started automatically

**Note**: If installation fails with error 1723 or 1603, ensure .NET 8.0 Runtime is installed. See [docs/INSTALLER-TROUBLESHOOTING.md](docs/INSTALLER-TROUBLESHOOTING.md) for details.

3. **Verify installation**
   - Open a browser and navigate to: `http://localhost:44325/api/v1/health`
   - You should see a JSON response with agent status
   - Check Windows Services (`services.msc`) for "Openctrol Agent" service

### Configuration

Configuration is stored at `C:\ProgramData\Openctrol\config.json`. The installer creates this file with your settings, or you can edit it manually:

```json
{
  "AgentId": "your-unique-agent-id",
  "HttpPort": 44325,
  "MaxSessions": 1,
  "CertPath": "",
  "CertPasswordEncrypted": "",
  "TargetFps": 30,
  "AllowedHaIds": [],
  "ApiKey": "your-api-key"
}
```

**Important Configuration Options:**

- **ApiKey**: Required for REST API authentication. Set this in the installer or edit `config.json` manually.
- **AllowedHaIds**: Array of Home Assistant instance IDs allowed to connect. Empty array = deny all (secure default).
- **HttpPort**: Port for the REST API and WebSocket (default: 44325).
- **CertPath** / **CertPasswordEncrypted**: For HTTPS support. Certificate password is encrypted with Windows DPAPI.

### After Installation

- **Service**: Runs as "Openctrol Agent" Windows service (automatic startup)
- **Config**: `C:\ProgramData\Openctrol\config.json`
- **Logs**: `C:\ProgramData\Openctrol\logs\` (daily rolling logs)
- **Event Log**: Windows Event Log → Application → Source: "OpenctrolAgent"

### Uninstall

1. Open "Apps & Features" (Windows Settings)
2. Find "Openctrol Agent"
3. Click "Uninstall"

**Note**: By default, configuration and logs in `C:\ProgramData\Openctrol` are preserved. To delete them, use:
```powershell
msiexec /x OpenctrolAgentSetup.msi CONFIG_DELETEPROGRAMDATA=1
```

## API Documentation

See [docs/API.md](docs/API.md) for complete REST API and WebSocket documentation.

### Quick Examples

**Health Check:**
```bash
curl http://localhost:44325/api/v1/health
```

**Create Desktop Session:**
```bash
curl -X POST http://localhost:44325/api/v1/sessions/desktop \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"ha_id": "home-assistant", "ttl_seconds": 900}'
```

**Get Audio State:**
```bash
curl http://localhost:44325/api/v1/audio/state \
  -H "X-API-Key: your-api-key"
```

## Documentation

- **[API Documentation](docs/API.md)** - Complete REST API and WebSocket protocol reference
- **[Architecture](docs/ARCHITECTURE.md)** - Internal architecture and design
- **[Build Guide](docs/BUILD.md)** - Building the agent from source
- **[Installer Guide](docs/INSTALLER.md)** - Building and using the MSI installer

## Development

### Building from Source

**Prerequisites:**
- .NET 8 SDK
- WiX Toolset v3.11+ (for building the installer)

**Build the Agent:**
```powershell
dotnet build
dotnet test
```

**Run as Console App (Development):**
```powershell
cd src/Openctrol.Agent
dotnet run
```

**Build the Installer:**

**Prerequisites:** WiX Toolset v3.11+ must be installed. See [docs/INSTALLER.md](docs/INSTALLER.md) for detailed build instructions.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

The installer will appear in the `dist/` folder as `OpenctrolAgentSetup.msi`.

**Note:** If you encounter build errors, ensure WiX Toolset is installed. Download from: https://wixtoolset.org/

### Project Structure

```
openctrol/
├── src/
│   └── Openctrol.Agent/          # Main Windows service
├── tests/
│   └── Openctrol.Agent.Tests/    # Unit tests
├── installer/
│   └── Openctrol.Agent.Setup/    # WiX installer project
├── scripts/
│   └── build-installer.ps1       # Build script
├── tools/
│   ├── install-service.ps1       # Manual service installation
│   └── uninstall-service.ps1     # Manual service removal
├── docs/                          # Documentation
│   ├── API.md                     # REST API and WebSocket documentation
│   ├── ARCHITECTURE.md            # Internal architecture and design
│   ├── BUILD.md                   # Building the agent from source
│   └── INSTALLER.md               # Building and using the MSI installer
└── dist/                          # Built installer artifacts
```

## Security

- **API Key Authentication**: All sensitive REST endpoints require an API key
- **HA ID Allowlist**: Empty allowlist = deny all (secure default)
- **Session Tokens**: Time-limited, cryptographically secure
- **HTTPS Support**: Optional but recommended for production
- **Local Network Only**: No internet exposure by default
- **Secure Config**: Config file has restrictive permissions (Administrators/SYSTEM only)

## Troubleshooting

**Service won't start:**
- Check Windows Event Log (Application log, source "OpenctrolAgent")
- Verify `config.json` is valid JSON
- Check that the configured port is not in use
- Try starting manually: `net start OpenctrolAgent`

**Can't connect to API:**
- Verify the service is running
- Check firewall rules (if firewall rule was created during install)
- Verify the port matches your configuration
- Check API key is correct in requests

**Screen capture not working:**
- Ensure the service is running as LocalSystem (default)
- Check Event Log for capture errors
- Verify desktop state (login screen, locked, or normal desktop)

**Audio control not working:**
- Verify NAudio dependencies are installed (included in installer)
- Check Event Log for audio initialization errors
- Ensure Windows Audio service is running

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please see the [Architecture documentation](docs/ARCHITECTURE.md) for design details and coding guidelines.
