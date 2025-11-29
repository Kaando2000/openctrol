# MSI Installer Custom Action Fixes

## Issue: "A DLL required for this Windows Installer package could not be run"

This document describes the fixes applied to resolve custom action DLL loading errors in the Openctrol Agent MSI installer.

## Root Causes Identified

1. **Architecture Mismatch**: CustomActions DLL was not explicitly built for x64, causing architecture mismatch with the MSI
2. **Missing Return Attribute**: Deferred custom actions were missing `Return="check"` for proper error handling
3. **Multiple Binary Entries**: Multiple Binary table entries pointing to the same DLL (redundant)
4. **Path Resolution**: WiX variable path needed to be absolute for proper resolution

## Fixes Applied

### 1. CustomActions.csproj - Architecture Fix

**File**: `installer/Openctrol.Agent.Setup/CustomActions/CustomActions.csproj`

**Changes**:
- Added `<PlatformTarget>x64</PlatformTarget>` to ensure DLL is built for x64
- Added `<Prefer32Bit>false</Prefer32Bit>` to prevent 32-bit preference
- Added `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` for explicit x64 targeting

**Why**: The MSI is built for x64, so the custom action DLL must also be x64. AnyCPU or x86 DLLs cannot be loaded by a 64-bit MSI.

### 2. Product.wxs - Custom Action Attributes

**File**: `installer/Openctrol.Agent.Setup/Product.wxs`

**Changes**:
- Added `Return="check"` to all deferred custom actions
- Consolidated multiple Binary entries into a single `CustomActionsBinary` entry
- All custom actions now reference the same Binary entry

**Before**:
```xml
<Binary Id="ConfigCustomActionBinary" SourceFile="$(var.CustomActions.TargetPath)" />
<Binary Id="ServiceCustomActionBinary" SourceFile="$(var.CustomActions.TargetPath)" />
<Binary Id="FirewallCustomActionBinary" SourceFile="$(var.CustomActions.TargetPath)" />
<Binary Id="ValidationCustomActionBinary" SourceFile="$(var.CustomActions.TargetPath)" />
```

**After**:
```xml
<Binary Id="CustomActionsBinary" SourceFile="$(var.CustomActions.TargetPath)" />
```

**Why**: 
- `Return="check"` ensures errors are properly reported
- Single Binary entry is more efficient and reduces chance of path resolution issues

### 3. Product.wxs - Custom Action Definitions

**All deferred custom actions now have**:
- `Execute="deferred"` - Runs with elevated privileges
- `Impersonate="no"` - Runs as SYSTEM (required for service operations)
- `Return="check"` - Properly reports errors

**Example**:
```xml
<CustomAction Id="ConfigCustomAction"
              BinaryKey="CustomActionsBinary"
              DllEntry="CreateConfigFile"
              Execute="deferred"
              Impersonate="no"
              Return="check" />
```

### 4. Build Script - Path Resolution

**File**: `scripts/build-installer.ps1`

**Changes**:
- Uses `Resolve-Path` to get absolute path for DLL
- Verifies DLL exists before building MSI
- Builds CustomActions with explicit x64 platform target

**Why**: WiX requires absolute paths for Binary table entries. Relative paths can fail during MSI execution.

### 5. ServiceControl Enhancement

**File**: `installer/Openctrol.Agent.Setup/Product.wxs`

**Changes**:
- Added `Start="install"` to ServiceControl element
- Kept custom action for better error handling and retry logic

**Why**: While WiX ServiceControl can start services, the custom action provides better error handling and retry logic for service startup.

## Custom Actions Summary

### Deferred Custom Actions (Run with elevated privileges)

1. **ConfigCustomAction** - Creates config.json file
   - Runs: After InstallFiles
   - DLL Entry: `CreateConfigFile`
   
2. **ServiceCustomAction** - Starts the Windows service
   - Runs: After InstallServices
   - DLL Entry: `InstallService`
   
3. **FirewallCustomAction** - Creates firewall rule
   - Runs: After ServiceCustomAction (if enabled)
   - DLL Entry: `CreateFirewallRule`
   
4. **UninstallFirewallCustomAction** - Removes firewall rule
   - Runs: Before RemoveFiles (on uninstall)
   - DLL Entry: `RemoveFirewallRule`
   
5. **UninstallProgramDataCustomAction** - Deletes ProgramData (optional)
   - Runs: After RemoveFiles (on uninstall, if enabled)
   - DLL Entry: `DeleteProgramData`

### Immediate Custom Actions (Run during UI phase)

1. **ValidateConfig** - Validates configuration input
   - Runs: On ConfigDlg Next button
   - DLL Entry: `ValidateConfig`
   
2. **GenerateApiKey** - Generates random API key
   - Runs: On ConfigDlg API Key Generate button
   - DLL Entry: `GenerateApiKey`

## Verification Steps

After applying fixes:

1. **Build CustomActions DLL**:
   ```powershell
   dotnet build installer\Openctrol.Agent.Setup\CustomActions\CustomActions.csproj -c Release -p:PlatformTarget=x64
   ```

2. **Verify DLL Architecture**:
   ```powershell
   $dll = [System.Reflection.Assembly]::LoadFrom("installer\Openctrol.Agent.Setup\CustomActions\bin\Release\net8.0-windows\CustomActions.dll")
   $dll.GetName().ProcessorArchitecture
   ```
   Should return `Amd64` (x64)

3. **Build MSI**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
   ```

4. **Test Installation with Logging**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\install-with-logging.ps1
   ```

5. **Check Log File**:
   - Look for "Begin ConfigCustomAction.CreateConfigFile"
   - Look for "Begin ServiceCustomAction.InstallService"
   - Verify no "DLL required" errors

## Troubleshooting

### Error: "CustomActions.dll not found"

**Solution**: Ensure CustomActions project is built before building MSI:
```powershell
dotnet build installer\Openctrol.Agent.Setup\CustomActions\CustomActions.csproj -c Release
```

### Error: "A DLL required for this Windows Installer package could not be run"

**Possible Causes**:
1. DLL architecture mismatch (DLL is x86 but MSI is x64)
2. Missing Microsoft.Deployment.WindowsInstaller.dll dependency
3. DLL path not resolving correctly in Binary table

**Solutions**:
1. Rebuild CustomActions with `-p:PlatformTarget=x64`
2. Verify DLL exists at the path specified in build script
3. Check MSI log file for detailed error messages

### Error: Custom action returns error code

**Check**:
- MSI log file for custom action error messages
- Windows Event Log for service-related errors
- Config file creation errors in ProgramData folder

## Files Modified

1. `installer/Openctrol.Agent.Setup/CustomActions/CustomActions.csproj`
2. `installer/Openctrol.Agent.Setup/Product.wxs`
3. `scripts/build-installer.ps1`
4. `scripts/install-with-logging.ps1` (new)

## Testing Checklist

- [ ] MSI builds without errors
- [ ] MSI installs without "DLL required" error
- [ ] Config file is created correctly
- [ ] Service installs and starts
- [ ] Firewall rule is created (if enabled)
- [ ] Service uninstalls cleanly
- [ ] Firewall rule is removed on uninstall
- [ ] ProgramData is preserved on uninstall (default)
- [ ] ProgramData is deleted on uninstall (if CONFIG_DELETEPROGRAMDATA=1)

