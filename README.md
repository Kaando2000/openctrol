# Openctrol

A Windows service that provides remote desktop control, audio management, and power control for Home Assistant over your local network.

## Project Structure

```
openctrol/
├── Agent/              # Windows Service (C#)
│   ├── src/            # Source code
│   ├── tests/          # Unit tests
│   └── setup/          # Installation scripts
├── HomeAssistant/      # Home Assistant Integration
│   ├── custom_components/openctrol/  # Python integration
│   └── www/openctrol/  # Frontend card (JavaScript)
└── docs/               # Documentation
```

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

3. **Verify installation**
   - Open a browser and navigate to: `http://localhost:44325/api/v1/health`
   - You should see a JSON response with agent status
   - Check Windows Services (`services.msc`) for "Openctrol Agent" service

### Home Assistant Integration

1. Copy `HomeAssistant/custom_components/openctrol` to your Home Assistant `custom_components` folder
2. Copy `HomeAssistant/www/openctrol` to your Home Assistant `www` folder
3. Restart Home Assistant
4. Add the integration via Configuration → Integrations
5. Add the card to your Lovelace dashboard

## Documentation

- [Complete Guide](docs/GUIDE.md) - Building, installation, configuration, API reference, and troubleshooting
- [Architecture](docs/ARCHITECTURE.md) - Comprehensive system architecture and how everything works

## Requirements

- **Windows 10/11** or Windows Server 2016+
- **.NET 8 Runtime** (if using pre-built binaries)
- **.NET 8 SDK** (if building from source)
- **Home Assistant** (for integration)
- **Administrator privileges** (for service installation)

## Security

- The agent runs as **LocalSystem** (required for desktop access)
- Configuration files have restrictive permissions (Administrators and SYSTEM only)
- API keys are auto-generated using cryptographically secure random number generation
- Certificate passwords are encrypted using Windows DPAPI (LocalMachine scope)
- The agent only listens on the local network (not exposed to internet)

See [SECURITY.md](SECURITY.md) for more details.

## License

See [LICENSE](LICENSE) file for details.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.
