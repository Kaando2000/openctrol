# Openctrol Agent Installer

This document describes how to build and use the WiX-based MSI installer for the Openctrol Windows Agent.

## Prerequisites

1. **WiX Toolset v3.11 or later**
   - Download from: https://wixtoolset.org/
   - Install the full WiX Toolset (includes Visual Studio extension if using VS)
   - Ensure `heat.exe`, `candle.exe`, and `light.exe` are in your PATH

2. **.NET 8 SDK**
   - Required to build the agent and custom actions

3. **Visual Studio 2019/2022** (optional but recommended)
   - For easier WiX project editing

## Building the Installer

### Method 1: Using the Build Script (Recommended)

The easiest way to build the installer is using the provided PowerShell script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

This script automatically:
1. Publishes the agent in Release mode for win-x64
2. Builds the custom actions DLL
3. Harvests files using `heat.exe`
4. Compiles and links the MSI using `candle.exe` and `light.exe`
5. Copies the final MSI to `dist/OpenctrolAgentSetup.msi`

**Output**: The installer will be at `dist/OpenctrolAgentSetup.msi` (typically 50-100 MB)

### Method 2: Manual Build Steps

If you prefer to build manually or need more control:

#### Step 1: Publish the Agent

```powershell
dotnet publish src\Openctrol.Agent\Openctrol.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

This creates the publish directory at:
```
src\Openctrol.Agent\bin\Release\net8.0-windows\win-x64\publish\
```

#### Step 2: Build Custom Actions

```powershell
dotnet build installer\Openctrol.Agent.Setup\CustomActions\CustomActions.csproj -c Release
```

#### Step 3: Harvest Files

```powershell
$publishDir = "src\Openctrol.Agent\bin\Release\net8.0-windows\win-x64\publish"
$wixBin = "C:\Program Files (x86)\WiX Toolset v3.14\bin"
& "$wixBin\heat.exe" dir $publishDir -cg AgentDependencies -gg -sfrag -srd -dr INSTALLFOLDER -var var.AgentPublishDir -out installer\Openctrol.Agent.Setup\HarvestFiles.wxs -t installer\Openctrol.Agent.Setup\HarvestFiles.xslt
```

#### Step 4: Build the MSI

The build script uses WiX command-line tools directly. If you want to use MSBuild instead:

```powershell
cd installer\Openctrol.Agent.Setup
msbuild Openctrol.Agent.Setup.wixproj /p:Configuration=Release /p:Platform=x64 /p:AgentPublishDir="$publishDir\" /v:minimal
```

**Note**: The build script method is recommended as it handles all the complexity automatically.

## Installer Features

### Installation Wizard Pages

1. **Welcome** - Standard welcome page
2. **License Agreement** - Shows license terms (if LICENSE.rtf exists)
3. **Install Location** - Default: `C:\Program Files\Openctrol`
4. **Configuration** - Configure:
   - HTTP Port (default: 44325, range: 1024-65535)
   - Use HTTPS (checkbox)
   - Certificate Path (if HTTPS enabled)
   - Certificate Password (if HTTPS enabled, encrypted with DPAPI)
   - API Key (auto-generated if empty)
   - Agent ID (auto-generated if empty)
5. **Firewall** - Option to create Windows Firewall rule
6. **Summary** - Review installation settings
7. **Install Progress** - Shows installation progress
8. **Finish** - Shows Agent ID, API Key, and health check URL

### What Gets Installed

- **Binaries**: `C:\Program Files\Openctrol\`
  - Openctrol.Agent.exe
  - Openctrol.Agent.dll
  - All runtime dependencies (.NET runtime, NAudio, etc.)

- **Configuration**: `C:\ProgramData\Openctrol\`
  - config.json (created with installer settings)
  - logs\ (created automatically)

- **Windows Service**: `OpenctrolAgent`
  - Display Name: "Openctrol Agent"
  - Start Type: Automatic
  - Account: LocalSystem
  - Service starts automatically after installation

- **Event Log Source**: `OpenctrolAgent`
  - Registered in Windows Event Log

- **Firewall Rule** (optional): "Openctrol Agent"
  - Inbound TCP rule for the configured port

### Configuration File Creation

The installer creates `C:\ProgramData\Openctrol\config.json` with:

- **AgentId**: Generated GUID (or user-provided)
- **HttpPort**: From installer UI
- **CertPath**: Certificate path (if HTTPS enabled)
- **CertPasswordEncrypted**: DPAPI-encrypted password (if provided)
- **ApiKey**: Generated secure key (or user-provided)
- **MaxSessions**: 1 (default)
- **TargetFps**: 30 (default)
- **AllowedHaIds**: Empty array (deny-all by default)

**Important**: 
- If `config.json` already exists, the installer will **NOT overwrite it**. This preserves existing configuration during upgrades.
- The installer reads the existing config file to display the correct Agent ID and API Key in the finish dialog.
- The config file is created with restrictive permissions (Administrators and SYSTEM only) for security.

### Security Features

- **Secure Properties**: API key and certificate password are marked as secure (not logged in MSI logs)
- **Safe Logging**: Custom actions never log actual secret values - only presence/absence indicators
- **File Permissions**: Config file has restrictive ACLs (Administrators and SYSTEM only, no inheritance)
- **DPAPI Encryption**: Certificate passwords are encrypted using Windows DPAPI (LocalMachine scope)
- **Auto-generated Secrets**: API keys and Agent IDs are cryptographically random (32 bytes for API key, GUID for Agent ID)
- **Config Preservation**: Existing config files are never overwritten during upgrades

## Uninstallation

The uninstaller performs the following steps in order:

1. **Stops the service**: Stops `OpenctrolAgent` if it is running
2. **Removes firewall rule**: Removes the "Openctrol Agent" firewall rule (if it exists)
3. **Removes binaries**: Deletes all files from `C:\Program Files\Openctrol`
4. **Removes service**: Unregisters the `OpenctrolAgent` Windows service
5. **Removes Event Log source**: Removes the "OpenctrolAgent" event log source
6. **ProgramData handling**: By default, **preserves** `C:\ProgramData\Openctrol` (config and logs remain)

### ProgramData Deletion

By default, the uninstaller **does NOT** delete `C:\ProgramData\Openctrol`. This preserves:
- Configuration file (`config.json`)
- Log files
- Any custom settings

To delete ProgramData during uninstall, you can:

**Option 1: Command-line property** (recommended for automation)
```powershell
msiexec /x Openctrol.Agent.Setup.msi CONFIG_DELETEPROGRAMDATA=1
```

**Option 2: Registry value** (set before uninstall)
- Set registry value: `HKEY_LOCAL_MACHINE\SOFTWARE\Openctrol\Agent\DeleteProgramDataOnUninstall` = `1`
- Then run normal uninstall

**Option 3: Manual deletion**
- After uninstall, manually delete `C:\ProgramData\Openctrol` if desired

**Note**: The installer does not currently provide a UI checkbox for ProgramData deletion during uninstall. This is by design to prevent accidental data loss. Use one of the methods above if you need to delete ProgramData.

## Manual Installation Alternative

If you prefer not to use the MSI installer, you can use the PowerShell scripts:

- `tools\install-service.ps1` - Installs the service manually
- `tools\uninstall-service.ps1` - Removes the service

## Troubleshooting

### Build Errors

**Error: "WiX Toolset not found"**
- Install WiX Toolset from https://wixtoolset.org/
- Ensure WiX is in your PATH or set `WixToolPath` property

**Error: "AgentPublishDir not found"**
- Ensure you've published the agent first (Step 1)
- Check that the publish directory exists

**Error: "CustomActions.dll not found"**
- Build the CustomActions project first (Step 2)

### Installation Errors

**Service fails to start**
- Check Windows Event Log (Application log, source "OpenctrolAgent") for detailed errors
- Verify `config.json` is valid JSON and located at `C:\ProgramData\Openctrol\config.json`
- Check that the configured port is not in use by another application
- Ensure LocalSystem account has necessary permissions
- Verify the service executable path is correct: `C:\Program Files\Openctrol\Openctrol.Agent.exe`
- Try starting the service manually: `net start OpenctrolAgent`
- Review installer logs (usually in `%TEMP%`) for service start errors

**Config file not created**
- Check that `C:\ProgramData\Openctrol` exists and is writable
- Review installer logs (usually in `%TEMP%`)

**Firewall rule not created**
- Check that you have administrator privileges
- Verify netsh.exe is available
- Check installer logs for firewall errors

## Advanced Configuration

### Customizing the Installer

- **Product Version**: Edit `Product.wxs`, change `<?define ProductVersion = "1.0.0" ?>`
- **Upgrade Code**: Change `<?define ProductUpgradeCode = "..." ?>` (keep same for upgrades)
- **Default Port**: Change `CONFIG_PORT` default value in `Product.wxs`
- **UI Text**: Edit dialog files in `UI\` directory

### Building for Different Architectures

The installer currently targets x64. To build for x86:

1. Publish agent for win-x86: `dotnet publish -c Release -r win-x86 --self-contained true`
2. Build installer: `msbuild Openctrol.Agent.Setup.wixproj /p:Platform=x86`

## Integration with CI/CD

Example build script:

```powershell
# Build agent
dotnet publish src\Openctrol.Agent\Openctrol.Agent.csproj -c Release -r win-x64 --self-contained true

# Build custom actions
dotnet build installer\Openctrol.Agent.Setup\CustomActions\CustomActions.csproj -c Release

# Build installer
msbuild installer\Openctrol.Agent.Setup\Openctrol.Agent.Setup.wixproj /p:Configuration=Release /p:Platform=x64

# MSI is in: installer\Openctrol.Agent.Setup\bin\Release\Openctrol.Agent.Setup.msi
```

## Upgrade Behavior

When upgrading an existing installation:

- **Binaries**: Replaced with new version
- **Service**: Service is stopped, binaries updated, then service restarted
- **Config**: Existing `config.json` is **preserved** (not overwritten)
- **ProgramData**: All logs and config remain intact
- **Firewall Rule**: Existing rule is preserved (not recreated)
- **Service Settings**: Service name, account, and startup type remain unchanged

**Important**: After upgrading, verify that the service starts successfully and check the Windows Event Log for any errors.

## Notes

- The installer requires administrator privileges (per-machine installation)
- All custom actions run with elevated privileges
- Secrets (API keys, passwords) are never logged in installer logs (only presence/absence is logged)
- The installer is designed to be upgrade-safe (preserves existing config)
- Service start failures do not fail the installation (service can be started manually)
- Firewall rule creation/removal failures are logged but do not fail installation/uninstallation

