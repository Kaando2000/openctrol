# Openctrol Agent

A Windows service that provides remote desktop control, power management, and audio control capabilities over a REST API and WebSocket interface.

## Features

- **Remote Desktop Control**
  - Real-time screen capture (GDI-based)
  - Mouse and keyboard input injection
  - Multi-monitor support
  - Works on login screen, locked desktop, and normal desktop

- **REST API**
  - Health monitoring
  - Session management
  - Power control (restart/shutdown)
  - Audio device and session control

- **WebSocket Streaming**
  - Binary frame streaming (JPEG encoded)
  - Low-latency input handling
  - Session-based authentication
  - Session expiry enforcement

## Requirements

- Windows 10/11 or Windows Server 2016+
- .NET 8 Runtime
- Administrator privileges (for service installation)

## Quick Start

### Building

```powershell
# Clone the repository
git clone <repository-url>
cd openctrol

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Running as Console App (Development)

```powershell
cd src/Openctrol.Agent
dotnet run
```

The service will start on `http://localhost:44325` by default.

### Installing as Windows Service

```powershell
# Run PowerShell as Administrator
.\tools\install-service.ps1
```

The service will be installed as "OpenctrolAgent" and start automatically.

### Uninstalling the Service

```powershell
# Run PowerShell as Administrator
.\tools\uninstall-service.ps1
```

## Configuration

Configuration is stored in `%ProgramData%\Openctrol\config.json`. A default configuration is created automatically on first run.

Example configuration:

```json
{
  "AgentId": "your-agent-id",
  "HttpPort": 44325,
  "MaxSessions": 1,
  "CertPath": "",
  "CertPasswordEncrypted": "",
  "TargetFps": 30,
  "AllowedHaIds": [],
  "ApiKey": ""
}
```

## API Documentation

See [docs/API.md](docs/API.md) for complete API documentation.

### Example: Create Desktop Session

```bash
curl -X POST http://localhost:44325/api/v1/sessions/desktop \
  -H "Content-Type: application/json" \
  -d '{"ha_id": "home-assistant", "ttl_seconds": 900}'
```

### Example: Health Check

```bash
curl http://localhost:44325/api/v1/health
```

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed architecture documentation.

## Development

### Project Structure

```
openctrol/
├── src/
│   └── Openctrol.Agent/          # Main service
├── tests/
│   └── Openctrol.Agent.Tests/    # Unit tests
├── tools/                         # Installation scripts
└── docs/                          # Documentation
```

### Running Tests

```powershell
dotnet test
```

## Security

- Session-based authentication with token expiration
- REST API authentication via API key (optional, configurable)
- Rate limiting on token validation failures
- HA ID allowlist support (deny-all by default when empty)
- HTTPS support with certificate configuration
- Secure by default: empty allowlist denies all access

## License

[Add your license here]

## Contributing

[Add contribution guidelines here]

