# Openctrol Agent Setup Guide

This folder contains the installation and uninstallation scripts for the Openctrol Windows Agent.

## Quick Start

### Installation

1. **Download or build the agent binaries**
   - Download the latest release ZIP, or
   - Build from source: `dotnet publish src\Openctrol.Agent\Openctrol.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false`

2. **Run the installer**
   ```powershell
   # Open PowerShell as Administrator (Right-click → "Run as Administrator")
   powershell -ExecutionPolicy Bypass -File .\setup\install.ps1
   ```

3. **Verify installation**
   - Check service status: `Get-Service -Name OpenctrolAgent`
   - Test health endpoint: `http://localhost:44325/api/v1/health`

### Installation Options

The installer supports various parameters for customization:

```powershell
# Basic installation with defaults
.\setup\install.ps1

# Custom port and API key
.\setup\install.ps1 -Port 8080 -ApiKey "my-secret-key"

# With HTTPS certificate
.\setup\install.ps1 -UseHttps -CertPath "C:\certs\cert.pfx" -CertPassword "password"

# Custom installation paths
.\setup\install.ps1 -InstallPath "D:\Openctrol" -ConfigPath "D:\OpenctrolConfig"

# Skip firewall rule creation
.\setup\install.ps1 -CreateFirewallRule:$false

# Specify source binaries location
.\setup\install.ps1 -SourcePath "C:\MyBuild\publish"
```

## What Gets Installed

- **Binaries**: `C:\Program Files\Openctrol\`
  - Openctrol.Agent.exe and all dependencies
  - Self-contained (includes .NET runtime)

- **Configuration**: `C:\ProgramData\Openctrol\`
  - `config.json` - Agent configuration
  - `logs\` - Application logs (created automatically)

- **Windows Service**: `OpenctrolAgent`
  - Display Name: "Openctrol Agent"
  - Start Type: Automatic
  - Account: LocalSystem
  - Auto-starts after installation

- **Event Log Source**: `OpenctrolAgent`
  - Registered in Windows Event Log → Application

- **Firewall Rule** (optional): "Openctrol Agent"
  - Inbound TCP rule for the configured port

## Configuration File

The installer creates `C:\ProgramData\Openctrol\config.json` with:

- **AgentId**: Auto-generated GUID
- **HttpPort**: From installer parameter (default: 44325)
- **ApiKey**: Auto-generated or user-provided
- **CertPath**: Certificate path (if HTTPS enabled)
- **CertPasswordEncrypted**: DPAPI-encrypted password (if provided)
- **MaxSessions**: 1 (default)
- **TargetFps**: 30 (default)
- **AllowedHaIds**: Empty array (deny-all by default)

**Important**: 
- If `config.json` already exists, the installer **preserves it** (upgrade-safe)
- The config file has restrictive permissions (Administrators and SYSTEM only)
- Certificate passwords are encrypted using Windows DPAPI (LocalMachine scope)

## Uninstallation

```powershell
# Uninstall (preserves configuration and logs)
.\setup\uninstall.ps1

# Uninstall and remove everything
.\setup\uninstall.ps1 -RemoveProgramData
```

The uninstaller:
1. Stops and deletes the Windows service
2. Removes firewall rules
3. Removes installation files
4. Optionally removes configuration and logs (if `-RemoveProgramData` is specified)

## Upgrading

The installer is **idempotent** - you can run it multiple times safely:

```powershell
# Upgrade to new version (preserves config, updates binaries, restarts service)
.\setup\install.ps1
```

Upgrade behavior:
- **Binaries**: Replaced with new version
- **Service**: Stopped, updated, then restarted
- **Config**: Preserved (not overwritten)
- **Logs**: Preserved

## Troubleshooting

**Service fails to start:**
- Check Windows Event Log (Application log, source "OpenctrolAgent")
- Verify `config.json` is valid JSON at `C:\ProgramData\Openctrol\config.json`
- Check that the configured port is not in use
- Try starting manually: `Start-Service -Name OpenctrolAgent`
- Review logs at `C:\ProgramData\Openctrol\logs\`

**Can't find binaries:**
- Ensure you've published the agent first
- Use `-SourcePath` parameter to specify the exact location
- The installer looks in: current directory, `.\bin`, and common build output locations

**Permission errors:**
- Ensure you're running PowerShell as Administrator
- Check that `C:\Program Files\Openctrol` and `C:\ProgramData\Openctrol` are writable

**Firewall rule not created:**
- Check that you have administrator privileges
- Verify Windows Firewall service is running
- You can create the rule manually or skip it with `-CreateFirewallRule:$false`

## Service Management

After installation, you can manage the service using PowerShell:

```powershell
# Start the service
Start-Service -Name OpenctrolAgent

# Stop the service
Stop-Service -Name OpenctrolAgent

# Check service status
Get-Service -Name OpenctrolAgent

# View service details
Get-Service -Name OpenctrolAgent | Format-List *
```

## Configuration Management

The configuration file is located at `C:\ProgramData\Openctrol\config.json`. You can edit it manually, but you must restart the service for changes to take effect:

```powershell
# Restart service to apply config changes
Restart-Service -Name OpenctrolAgent
```

**Note**: The service must be running as LocalSystem (default) to access the desktop for screen capture.

## Security Considerations

- **Config File Permissions**: Config files have restrictive ACLs (Administrators and SYSTEM only)
- **Certificate Passwords**: Encrypted using Windows DPAPI (LocalMachine scope)
- **API Keys**: Auto-generated using cryptographically secure random number generation
- **Service Account**: Runs as LocalSystem (required for desktop access)
- **Firewall Rules**: Created only if explicitly requested

## Local Web UI

After installation, you can access a local web-based control panel:

**URL**: `http://localhost:<port>/ui` (or `https://localhost:<port>/ui` if HTTPS is enabled)

**Note**: The UI is only accessible from the local machine (localhost) for security.

The UI provides:
- **Service Status**: View agent health, desktop state, and active sessions
- **Configuration**: View and edit basic configuration (port, HTTPS, API key, allowed HA IDs)
- **Service Controls**: Start, stop, restart the Windows service
- **Uninstall**: Trigger uninstallation (if uninstall script is present)

**Important**: 
- The UI is restricted to localhost only - it cannot be accessed from other machines on the network
- Configuration changes require a service restart to take effect
- The UI does not expose sensitive information (API keys, certificate passwords)

## Additional Resources

- [API Documentation](../docs/API.md) - Complete REST API and WebSocket documentation
- [Architecture Documentation](../docs/ARCHITECTURE.md) - Internal architecture and design
- [Build Guide](../docs/BUILD.md) - Building the agent from source

