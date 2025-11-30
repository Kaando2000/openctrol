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

### Installation (Recommended)

The **PowerShell installer** is the recommended and supported installation method. It's simple, reliable, and doesn't require additional tools.

1. **Download and extract the release**
   - Download the latest release ZIP from the [Releases](https://github.com/yourusername/openctrol/releases) page
   - Extract the ZIP file to a folder (e.g., `C:\Openctrol`)

2. **Run the installer**
   - Open PowerShell as Administrator (Right-click → "Run as Administrator")
   - Navigate to the extracted folder
   - Run the installer:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\setup\install.ps1
     ```
   - The installer will:
     - Copy binaries to `C:\Program Files\Openctrol`
     - Create configuration at `C:\ProgramData\Openctrol\config.json`
     - Install and start the Windows service
     - Optionally create a firewall rule

3. **Configure during installation** (optional parameters)
   ```powershell
   # Custom port and API key
   .\setup\install.ps1 -Port 8080 -ApiKey "my-secret-key"
   
   # With HTTPS
   .\setup\install.ps1 -UseHttps -CertPath "C:\certs\cert.pfx" -CertPassword "password"
   
   # Skip firewall rule
   .\setup\install.ps1 -CreateFirewallRule:$false
   ```

4. **Verify installation**
   - Open a browser and navigate to: `http://localhost:44325/api/v1/health`
   - You should see a JSON response with agent status
   - Check Windows Services (`services.msc`) for "Openctrol Agent" service

**Installation Locations:**
- **Binaries**: `C:\Program Files\Openctrol`
- **Configuration**: `C:\ProgramData\Openctrol\config.json`
- **Logs**: `C:\ProgramData\Openctrol\logs\`
- **Service**: `OpenctrolAgent` (Windows Service)


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

**Using PowerShell (Recommended):**
```powershell
# Uninstall (preserves configuration and logs)
powershell -ExecutionPolicy Bypass -File .\setup\uninstall.ps1

# Uninstall and remove configuration/logs
powershell -ExecutionPolicy Bypass -File .\setup\uninstall.ps1 -RemoveProgramData
```

**Note**: By default, configuration and logs in `C:\ProgramData\Openctrol` are preserved. Use `-RemoveProgramData` to delete them.

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
- **[Setup Guide](setup/README.md)** - Complete installation and setup instructions

## Development

### Building from Source

**Prerequisites:**
- .NET 8 SDK

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

**Build the Agent for Distribution:**

```powershell
# Publish self-contained agent
dotnet publish src\Openctrol.Agent\Openctrol.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

The published binaries will be in `src\Openctrol.Agent\bin\Release\net8.0-windows\win-x64\publish\`


### Project Structure

```
openctrol/
├── src/
│   └── Openctrol.Agent/          # Main Windows service
├── tests/
│   └── Openctrol.Agent.Tests/    # Unit tests
├── setup/
│   ├── README.md                    # Setup guide and instructions
│   ├── install.ps1                  # PowerShell installer
│   └── uninstall.ps1                 # PowerShell uninstaller
├── docs/                          # Documentation
│   ├── API.md                     # REST API and WebSocket documentation
│   ├── ARCHITECTURE.md            # Internal architecture and design
│   ├── BUILD.md                   # Building the agent from source
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
