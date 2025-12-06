# Build Instructions

## Prerequisites

- .NET 8 SDK or later
- Windows 10/11 or Windows Server 2016+
- Administrator privileges (for service installation)

## Building the Solution

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

## Project Structure

```
openctrol/
├── src/
│   └── Openctrol.Agent/          # Main service project
├── tests/
│   └── Openctrol.Agent.Tests/    # Unit tests
├── setup/
│   ├── README.md                 # Setup guide
│   ├── install.ps1               # PowerShell installer
│   └── uninstall.ps1              # PowerShell uninstaller
└── docs/                          # Documentation
```

## Output

After building, the executable will be located at:
- Debug: `src/Openctrol.Agent/bin/Debug/net8.0-windows/Openctrol.Agent.exe`
- Release: `src/Openctrol.Agent/bin/Release/net8.0-windows/Openctrol.Agent.exe`

## Publishing for Deployment

To create a deployment-ready package, you need to **publish** the agent. The build script automates this process:

### Using the Build Script (Recommended)

```powershell
# Build and publish with default settings (Release, win-x64, self-contained)
.\tools\build-agent.ps1

# Build with tests and create ZIP package
.\tools\build-agent.ps1 -RunTests -CreateZip

# Publish Debug build
.\tools\build-agent.ps1 -Configuration Debug
```

### Manual Publish Command

```powershell
# Publish self-contained agent (recommended for deployment)
dotnet publish src\Openctrol.Agent\Openctrol.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false

# Output location:
# src/Openctrol.Agent/bin/Release/net8.0-windows/win-x64/publish/
```

**Important**: The installer script automatically finds published binaries if they're in the standard location.

## Dependencies

The project uses the following NuGet packages:
- `Microsoft.Extensions.Hosting.WindowsServices` - Windows Service hosting
- `Microsoft.AspNetCore.App` - Web framework (implicit)
- `NAudio` - Audio management
- `System.Drawing.Common` - Image encoding

All dependencies are automatically restored during build.

## Running as Console App (Development)

For development and testing, you can run the service as a console application:

```powershell
cd src/Openctrol.Agent
dotnet run
```

The service will start and listen on the configured port (default: 44325).

## Installing as Windows Service

### Quick Install (Automated)

1. **Build and publish the agent:**
   ```powershell
   .\tools\build-agent.ps1
   ```

2. **Run the installer (as Administrator):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\setup\install.ps1
   ```

The installer will automatically find the published binaries in the standard location.

### Manual Install

If you've published to a custom location:

```powershell
# Specify source path explicitly
.\setup\install.ps1 -SourcePath "C:\MyBuild\publish"
```

See the [Setup Guide](../setup/README.md) for complete installation instructions and options.

## Troubleshooting

### Build Errors

- **Missing .NET 8 SDK**: Install from https://dotnet.microsoft.com/download
- **NuGet restore issues**: Run `dotnet restore` manually
- **Windows-specific APIs**: Ensure you're building on Windows

### Runtime Errors

- **Port already in use**: Change the port in `%ProgramData%\Openctrol\config.json`
- **Certificate errors**: Ensure certificate path and password are correct in config
- **Permission errors**: Run as Administrator or configure appropriate service account

## Configuration

The service reads configuration from:
`%ProgramData%\Openctrol\config.json`

A default configuration file is created automatically on first run if it doesn't exist.

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed configuration options.

