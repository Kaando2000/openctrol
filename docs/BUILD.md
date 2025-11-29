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
├── installer/
│   └── Openctrol.Agent.Setup/    # WiX installer project
├── scripts/
│   └── build-installer.ps1       # Build script for MSI
├── tools/                         # Service installation scripts
└── docs/                          # Documentation
```

## Output

After building, the executable will be located at:
- Debug: `src/Openctrol.Agent/bin/Debug/net8.0-windows/Openctrol.Agent.exe`
- Release: `src/Openctrol.Agent/bin/Release/net8.0-windows/Openctrol.Agent.exe`

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

See `tools/install-service.ps1` for service installation instructions.

```powershell
# Run as Administrator
.\tools\install-service.ps1
```

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

