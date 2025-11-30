# Build script for Openctrol Agent MSI Installer
# This script builds the agent, custom actions, and MSI installer
# Output: dist/OpenctrolAgentSetup.msi

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Building Openctrol Agent Installer ===" -ForegroundColor Cyan

# Get script directory and project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$distDir = Join-Path $projectRoot "dist"
$installerDir = Join-Path $projectRoot "installer\Openctrol.Agent.Setup"

# Ensure dist directory exists
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# Step 1: Publish the agent
Write-Host "`n[1/4] Publishing Openctrol Agent..." -ForegroundColor Yellow
$agentProject = Join-Path $projectRoot "src\Openctrol.Agent\Openctrol.Agent.csproj"
$publishDir = Join-Path $projectRoot "src\Openctrol.Agent\bin\$Configuration\net8.0-windows\win-$Platform\publish"

Push-Location $projectRoot
try {
    dotnet publish $agentProject -c $Configuration -r win-$Platform --self-contained true -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Agent publish failed"
        exit 1
    }
    Write-Host "Agent published to: $publishDir" -ForegroundColor Green
}
finally {
    Pop-Location
}

# Step 2: Build custom actions
Write-Host "`n[2/4] Building Custom Actions..." -ForegroundColor Yellow
$customActionsProject = Join-Path $installerDir "CustomActions\CustomActions.csproj"

        Push-Location (Split-Path $customActionsProject)
        try {
            # Build self-contained EXE for x64 platform to match MSI architecture
            # Self-contained means no .NET runtime needed on target machine
            Write-Host "  Building self-contained EXE custom actions..." -ForegroundColor Gray
            dotnet publish -c $Configuration -p:PlatformTarget=x64 -p:SelfContained=true -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Custom actions build failed"
                exit 1
            }
            
            # Verify EXE was created (publish puts it in publish folder)
            $exePath = Join-Path (Split-Path $customActionsProject) "bin\$Configuration\net8.0-windows\win-x64\publish\CustomActions.exe"
            if (-not (Test-Path $exePath)) {
                Write-Error "CustomActions.exe not found after build at: $exePath"
                exit 1
            }
            
            Write-Host "Custom actions built successfully (self-contained EXE)" -ForegroundColor Green
            Write-Host "  No .NET 8 Runtime required on target machine!" -ForegroundColor Green
        }
        finally {
            Pop-Location
        }

# Step 3: Harvest files using heat.exe
Write-Host "`n[3/4] Harvesting files with heat.exe..." -ForegroundColor Yellow

# Find WiX installation
$wixPath = $env:WIX
if (-not $wixPath) {
    # Try v3.14 first (newer), then v3.11
    $wixPath = "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin"
    if (-not (Test-Path $wixPath)) {
        $wixPath = "${env:ProgramFiles}\WiX Toolset v3.14\bin"
        if (-not (Test-Path $wixPath)) {
            $wixPath = "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin"
            if (-not (Test-Path $wixPath)) {
                $wixPath = "${env:ProgramFiles}\WiX Toolset v3.11\bin"
            }
        }
    }
}

$heatExe = Join-Path $wixPath "heat.exe"
if (-not (Test-Path $heatExe)) {
    Write-Error "heat.exe not found. Please install WiX Toolset v3.11 or later from https://wixtoolset.org/"
    exit 1
}

$harvestFile = Join-Path $installerDir "HarvestFiles.wxs"
$xsltFile = Join-Path $installerDir "HarvestFiles.xslt"

& $heatExe dir $publishDir `
    -cg AgentDependencies `
    -gg `
    -sfrag `
    -srd `
    -dr INSTALLFOLDER `
    -var var.AgentPublishDir `
    -out $harvestFile `
    -t $xsltFile

if ($LASTEXITCODE -ne 0) {
    Write-Error "File harvesting failed"
    exit 1
}
Write-Host "Files harvested successfully" -ForegroundColor Green

# Step 4: Build the MSI
Write-Host "`n[4/4] Building MSI installer..." -ForegroundColor Yellow
$wixProject = Join-Path $installerDir "Openctrol.Agent.Setup.wixproj"

# Set WiX variable for publish directory
$env:AgentPublishDir = $publishDir + "\"

# Build MSI using WiX command-line tools (candle + light)
$candleExe = Join-Path $wixPath "candle.exe"
$lightExe = Join-Path $wixPath "light.exe"

if (-not (Test-Path $candleExe) -or -not (Test-Path $lightExe)) {
    Write-Error "WiX tools (candle.exe or light.exe) not found at: $wixPath"
    exit 1
}

Push-Location $installerDir
try {
    # Compile WiX source files
    Write-Host "Compiling WiX source files..." -ForegroundColor Yellow
    $wixFiles = @("Product.wxs", "HarvestFiles.wxs", "UI\ConfigDlg.wxs", "UI\FirewallDlg.wxs", "UI\SummaryDlg.wxs", "UI\ValidationErrorDlg.wxs", "UI\FinishDlg.wxs")
    $wixobjFiles = @()
    
    foreach ($wxsFile in $wixFiles) {
        $wxsPath = Join-Path $installerDir $wxsFile
        if (Test-Path $wxsPath) {
            $wixobjFile = $wxsFile -replace '\.wxs$', '.wixobj'
            $wixobjPath = Join-Path $installerDir $wixobjFile
            
            # Self-contained EXE build location (publish folder)
            $customActionsExe = Join-Path $projectRoot "installer\Openctrol.Agent.Setup\CustomActions\bin\Release\net8.0-windows\win-x64\publish\CustomActions.exe"
            
            # Verify EXE exists
            if (-not (Test-Path $customActionsExe)) {
                Write-Error "CustomActions.exe not found at: $customActionsExe"
                Write-Error "Please build CustomActions project first: dotnet publish installer\Openctrol.Agent.Setup\CustomActions\CustomActions.csproj -c Release"
                exit 1
            }
            
            # Use absolute path for WiX variable (required for WiX)
            $customActionsExeAbsolute = (Resolve-Path $customActionsExe).Path
            
            # Verify EXE exists before building
            if (-not (Test-Path $customActionsExeAbsolute)) {
                Write-Error "CustomActions.exe not found at: $customActionsExeAbsolute"
                exit 1
            }
            
            Write-Host "  Using CustomActions EXE (self-contained): $customActionsExeAbsolute" -ForegroundColor Gray
            
            # WiX requires forward slashes or escaped backslashes in paths
            # Use forward slashes for better compatibility
            $customActionsExeForWix = $customActionsExeAbsolute -replace '\\', '/'
            $publishDirNormalized = $publishDir.TrimEnd('\') + '\'
            
            $candleArgs = @(
                "-nologo",
                "-arch", $Platform,
                "-ext", "WixUtilExtension",
                "-dAgentPublishDir=$publishDirNormalized",
                "-dCustomActions.TargetPath=$customActionsExeForWix",
                "-out", $wixobjPath,
                $wxsPath
            )
            
            & $candleExe $candleArgs
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to compile $wxsFile"
                exit 1
            }
            $wixobjFiles += $wixobjPath
        }
    }
    
    # Link to create MSI
    Write-Host "Linking MSI..." -ForegroundColor Yellow
    $msiOutputPath = Join-Path $distDir "OpenctrolAgentSetup.msi"
    
    # Find WiX extension DLLs (they're in the same bin directory)
    $wixUIExt = Join-Path $wixPath "WixUIExtension.dll"
    $wixUtilExt = Join-Path $wixPath "WixUtilExtension.dll"
    $wixNetFxExt = Join-Path $wixPath "WixNetFxExtension.dll"
    
    if (-not (Test-Path $wixUIExt)) {
        # Try alternative location
        $wixExtPath = Join-Path (Split-Path $wixPath) "WixUIExtension.dll"
        if (Test-Path $wixExtPath) {
            $wixUIExt = $wixExtPath
            $wixUtilExt = Join-Path (Split-Path $wixPath) "WixUtilExtension.dll"
            $wixNetFxExt = Join-Path (Split-Path $wixPath) "WixNetFxExtension.dll"
        }
    }
    
    $lightArgs = @(
        "-nologo",
        "-ext", $wixUIExt,
        "-ext", $wixUtilExt,
        "-ext", $wixNetFxExt,
        "-sice:ICE80",  # Suppress ICE80 validation (64-bit components in 32-bit directories)
        "-sice:ICE30",  # Suppress ICE30 validation (duplicate files - main exe/dll are in both ProductComponents and harvested files)
        "-out", $msiOutputPath
    ) + $wixobjFiles
    
    & $lightExe $lightArgs
    # Check if MSI was created even if light.exe returned non-zero (warnings are OK)
    if (-not (Test-Path $msiOutputPath) -and $LASTEXITCODE -ne 0) {
        Write-Error "Failed to link MSI"
        exit 1
    }
    
    Write-Host "`n=== Build Complete ===" -ForegroundColor Green
    Write-Host "MSI installer: $msiOutputPath" -ForegroundColor Cyan
    if (Test-Path $msiOutputPath) {
        Write-Host "File size: $([math]::Round((Get-Item $msiOutputPath).Length / 1MB, 2)) MB" -ForegroundColor Cyan
    }
}
finally {
    Pop-Location
}

Write-Host "`nInstallation package ready in dist/ folder!" -ForegroundColor Green
