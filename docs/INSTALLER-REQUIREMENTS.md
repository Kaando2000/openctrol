# MSI Installer Requirements

## Prerequisites

### Required Before Installation

**⚠ CRITICAL: .NET 8.0 Runtime must be installed BEFORE running the MSI installer.**

The installer uses .NET 8 custom actions that require the .NET 8.0 Runtime to be present on the target machine.

#### How to Install .NET 8.0 Runtime

1. **Download .NET 8.0 Runtime**
   - Visit: https://dotnet.microsoft.com/download/dotnet/8.0
   - Download either:
     - **Desktop Runtime** (recommended for Windows desktop apps)
     - **ASP.NET Core Runtime** (includes Desktop Runtime)

2. **Install the Runtime**
   - Run the downloaded installer
   - Follow the installation wizard
   - Restart if prompted

3. **Verify Installation**
   ```powershell
   dotnet --list-runtimes
   ```
   Should show:
   ```
   Microsoft.NETCore.App 8.0.x [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
   ```

#### What Happens Without .NET 8 Runtime?

If you try to install the MSI without .NET 8.0 Runtime:
- Installation will fail with **Error 1723** or **Error 1603**
- Windows Event Log will show: "A DLL required for this install to complete could not be run"
- The custom actions cannot execute because Windows Installer cannot load .NET 8 assemblies

## System Requirements

- **Operating System**: Windows 10 (version 1809 or later) or Windows 11
- **Architecture**: x64 (64-bit)
- **.NET Runtime**: .NET 8.0 Desktop Runtime or ASP.NET Core Runtime
- **Privileges**: Administrator (for installation)

## Installation Steps

1. **Install .NET 8.0 Runtime** (if not already installed)
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Run the installer
   - Verify with: `dotnet --list-runtimes`

2. **Run the MSI Installer**
   - Right-click `OpenctrolAgentSetup.msi`
   - Select "Install"
   - Follow the installation wizard

3. **Verify Installation**
   - Check Windows Services for "Openctrol Agent"
   - Visit: `http://localhost:44325/api/v1/health`

## Troubleshooting

### Error 1723 / 1603

**Cause**: .NET 8.0 Runtime is not installed.

**Solution**:
1. Install .NET 8.0 Runtime from https://dotnet.microsoft.com/download/dotnet/8.0
2. Verify installation: `dotnet --list-runtimes`
3. Retry MSI installation

### Check Installation Log

```powershell
msiexec /i dist\OpenctrolAgentSetup.msi /l*v install.log
```

Look for:
- "CustomActionsBinary" (should be in Binary table)
- "Error 1723" or "Cannot load DLL"
- ".NET" or "runtime" related errors

## Future Improvements

For future releases, consider:
- Creating a bootstrapper that checks for and installs .NET 8 Runtime
- Using WiX's `WixNetFxExtension` to detect .NET runtime
- Providing a combined installer that includes .NET 8 Runtime

