# Installation Guide

The Openctrol Agent uses a PowerShell-based installer for installation and setup.

## Quick Start

See the [Setup Guide](../setup/README.md) for complete installation instructions.

**Quick Installation:**
```powershell
powershell -ExecutionPolicy Bypass -File .\setup\install.ps1
```

The PowerShell installer is the **recommended and supported** installation method. It's simple, reliable, and doesn't require additional tools or dependencies.

## Installation Methods

The installer supports various parameters for customization. See [Setup Guide](../setup/README.md) for detailed options and examples.

## Uninstallation

```powershell
# Uninstall (preserves configuration and logs)
.\setup\uninstall.ps1

# Uninstall and remove everything
.\setup\uninstall.ps1 -RemoveProgramData
```

## Additional Resources

- [Setup Guide](../setup/README.md) - Complete setup instructions
- [API Documentation](API.md) - REST API and WebSocket documentation
- [Architecture Documentation](ARCHITECTURE.md) - Internal architecture and design
- [Build Guide](BUILD.md) - Building the agent from source
