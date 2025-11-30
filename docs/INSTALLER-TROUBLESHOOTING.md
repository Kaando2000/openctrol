# MSI Installer Troubleshooting

## Error 1723: "A DLL required for this install to complete could not be run"

### Symptoms
- Installation fails with error 1723
- Windows Event Log shows error 1603 (Fatal error during installation)
- MSI log shows: "Error 1723. There is a problem with this Windows Installer package. A DLL required for this install to complete could not be run."
- Error occurs when custom actions try to execute

### Root Cause
Windows Installer cannot load the .NET custom action DLL. This typically happens when:

1. **.NET 8 Runtime Not Installed** (MOST COMMON): The custom actions are built for .NET 8 and require the .NET 8 runtime to be installed on the target machine. Windows Installer's custom action host (ca.dll) cannot load .NET 8 assemblies without the runtime.

2. **Architecture Mismatch**: The DLL architecture (x86/x64) doesn't match the MSI architecture.

3. **Missing Dependencies**: Required DLLs (like `Microsoft.Deployment.WindowsInstaller.dll`) are not available, though this is usually in the GAC.

### Solution

#### Option 1: Install .NET 8 Runtime (Recommended)
1. Download and install .NET 8.0 Runtime from: https://dotnet.microsoft.com/download/dotnet/8.0
2. Select "Desktop Runtime" or "ASP.NET Core Runtime" (both include the runtime)
3. Run the installer again

#### Option 2: Verify .NET Runtime Installation
```powershell
# Check if .NET 8 is installed
dotnet --list-runtimes

# Should show something like:
# Microsoft.NETCore.App 8.0.x [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
```

#### Option 3: Check MSI Log for Details
```powershell
# Install with verbose logging
msiexec /i dist\OpenctrolAgentSetup.msi /l*v install.log

# Check install.log for:
# - "CustomActionsBinary" (should be in Binary table)
# - "Cannot load DLL" errors
# - ".NET" or "runtime" related errors
```

### Verification Steps

1. **Check DLL Architecture**:
   ```powershell
   # The DLL should be x64 to match the MSI
   # Use a tool like dumpbin or check the PE header
   ```

2. **Verify Binary Table**:
   - The MSI should contain `CustomActionsBinary` in its Binary table
   - This can be verified using Orca or similar MSI editing tools

3. **Check Dependencies**:
   - `Microsoft.Deployment.WindowsInstaller.dll` should be available
   - .NET 8 runtime must be installed

### Prevention

For future releases, consider:
- Adding a .NET 8 runtime prerequisite check in the MSI
- Using WiX's `WixNetFxExtension` to detect .NET runtime
- Providing a bootstrapper that installs .NET 8 if missing

### Alternative: Use Native Custom Actions

If .NET runtime dependency is problematic, consider:
- Converting critical custom actions to native C++ DLLs
- Using WiX's built-in capabilities (ServiceInstall, FirewallExtension) where possible
- Using PowerShell scripts as custom actions (though less reliable)

## Error 1603: Fatal Error During Installation

This is a generic error that wraps error 1723. Check the detailed MSI log for the actual cause.

## Common Issues

### Custom Action DLL Not Found
- **Symptom**: Error about missing DLL in Binary table
- **Fix**: Verify WiX variable `CustomActions.TargetPath` is set correctly in build script

### Architecture Mismatch
- **Symptom**: DLL loads but fails to execute
- **Fix**: Ensure DLL is built for x64: `dotnet build -p:PlatformTarget=x64`

### Missing .NET Runtime
- **Symptom**: Error 1723 during custom action execution
- **Fix**: Install .NET 8.0 Runtime on target machine

