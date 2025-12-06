# Openctrol Agent Complete Setup Script
# This script builds, publishes, and installs the Openctrol Agent
# Designed to be portable and reusable for GitHub users
# Requires Administrator privileges

<#
.SYNOPSIS
    Complete setup script for Openctrol Agent - builds, publishes, and installs.

.DESCRIPTION
    This script:
    1. Builds the Openctrol Agent project
    2. Publishes it to setup/bin (self-contained)
    3. Uninstalls any existing version
    4. Installs the new version
    5. Verifies installation

    The setup folder becomes portable and can be distributed independently.

.PARAMETER SkipBuild
    Skip the build/publish step (use existing binaries in setup/bin)

.PARAMETER SkipUninstall
    Skip uninstalling existing version (useful for upgrades)

.PARAMETER Port
    HTTP port for the agent (default: 44325)

.PARAMETER ApiKey
    API key for authentication (if not provided, will be generated)

.PARAMETER UseHttps
    Enable HTTPS (requires certificate)

.PARAMETER CertPath
    Path to certificate file (required if UseHttps is true)

.PARAMETER CertPassword
    Certificate password (optional, will be encrypted with DPAPI)

.PARAMETER CreateFirewallRule
    Create Windows Firewall rule (default: $true)

.EXAMPLE
    .\setup.ps1

.EXAMPLE
    .\setup.ps1 -Port 8080 -ApiKey "my-secret-key"

.EXAMPLE
    .\setup.ps1 -SkipBuild  # Use existing binaries
#>

param(
    [switch]$SkipBuild = $false,
    [switch]$SkipUninstall = $false,
    [int]$Port = 44325,
    [string]$ApiKey = "",
    [switch]$UseHttps = $false,
    [string]$CertPath = "",
    [string]$CertPassword = "",
    [bool]$CreateFirewallRule = $true
)

$ErrorActionPreference = "Stop"

# Check if running as admin
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Elevate if not admin
if (-not (Test-Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    $scriptPath = $MyInvocation.MyCommand.Path
    $argList = "-ExecutionPolicy Bypass -File `"$scriptPath`""
    if ($SkipBuild) { $argList += " -SkipBuild" }
    if ($SkipUninstall) { $argList += " -SkipUninstall" }
    if ($Port -ne 44325) { $argList += " -Port $Port" }
    if ($ApiKey) { $argList += " -ApiKey `"$ApiKey`"" }
    if ($UseHttps) { $argList += " -UseHttps" }
    if ($CertPath) { $argList += " -CertPath `"$CertPath`"" }
    if ($CertPassword) { $argList += " -CertPassword `"$CertPassword`"" }
    if (-not $CreateFirewallRule) { $argList += " -CreateFirewallRule:`$false" }
    
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait
    exit $LASTEXITCODE
}

Write-Host "=== Openctrol Agent Complete Setup ===" -ForegroundColor Cyan
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$agentRoot = Split-Path -Parent $scriptDir
$projectRoot = Split-Path -Parent $agentRoot
$serviceName = "OpenctrolAgent"
$publishPath = Join-Path $scriptDir "bin"
$projectPath = Join-Path $agentRoot "src\Openctrol.Agent.csproj"

# Step 1: Build and Publish (unless skipped)
if (-not $SkipBuild) {
    Write-Host "[1/5] Building and publishing agent..." -ForegroundColor Yellow
    
    if (-not (Test-Path $projectPath)) {
        throw "Project file not found: $projectPath. Please run this script from the setup folder or ensure the project structure is correct."
    }
    
    # Clean publish directory
    if (Test-Path $publishPath) {
        Write-Host "  Cleaning existing publish directory..." -ForegroundColor Gray
        Remove-Item -Path $publishPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
    
            # Build solution first (to catch any errors early)
            Write-Host "  Building solution..." -ForegroundColor Gray
            Push-Location $projectRoot
            try {
                # Build only the main project (skip tests for faster setup)
                dotnet build $projectPath -c Release 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Build failed with exit code $LASTEXITCODE"
                }
                Write-Host "  Build successful" -ForegroundColor Green
            }
            finally {
                Pop-Location
            }
            
            # Publish to setup/bin
            Write-Host "  Publishing to setup/bin..." -ForegroundColor Gray
            Push-Location $agentRoot
            try {
                dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishPath 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Publish failed with exit code $LASTEXITCODE"
                }
                Write-Host "  Publish successful" -ForegroundColor Green
            }
            finally {
                Pop-Location
            }
    
    # Verify publish output
    $exePath = Join-Path $publishPath "Openctrol.Agent.exe"
    if (-not (Test-Path $exePath)) {
        throw "Published executable not found: $exePath"
    }
    Write-Host "  Published binaries ready at: $publishPath" -ForegroundColor Green
} else {
    Write-Host "[1/5] Skipping build (using existing binaries)" -ForegroundColor Yellow
    if (-not (Test-Path (Join-Path $publishPath "Openctrol.Agent.exe"))) {
        throw "Binaries not found in $publishPath. Run without -SkipBuild to build first."
    }
}

# Step 2: Uninstall existing version (unless skipped)
if (-not $SkipUninstall) {
    Write-Host "[2/5] Uninstalling existing version..." -ForegroundColor Yellow
    
    $uninstallScript = Join-Path $scriptDir "uninstall.ps1"
    if (Test-Path $uninstallScript) {
        try {
            & $uninstallScript -SkipFirewallCleanup:$false 2>&1 | Out-Null
            Write-Host "  Uninstall completed" -ForegroundColor Green
        } catch {
            Write-Host "  Uninstall script had warnings (may be normal if not installed): $_" -ForegroundColor Yellow
        }
    } else {
        # Manual uninstall if script doesn't exist
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service) {
            if ($service.Status -eq "Running") {
                Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
            }
            sc.exe delete $serviceName | Out-Null
            Start-Sleep -Seconds 1
        }
        
        # Kill any running processes
        Get-Process -Name "Openctrol.Agent" -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 1
        
        Write-Host "  Existing version removed" -ForegroundColor Green
    }
} else {
    Write-Host "[2/5] Skipping uninstall (upgrade mode)" -ForegroundColor Yellow
}

# Step 3: Install new version
Write-Host "[3/5] Installing agent..." -ForegroundColor Yellow

# Use install.ps1 as a helper script (it handles all the installation details)
$installScript = Join-Path $scriptDir "install.ps1"
if (-not (Test-Path $installScript)) {
    throw "Install script not found: $installScript"
}

# Build install parameters
$installParams = @{
    SourcePath = $publishPath
    Port = $Port
    CreateFirewallRule = $CreateFirewallRule
}

if ($ApiKey) {
    $installParams.ApiKey = $ApiKey
}

if ($UseHttps) {
    $installParams.UseHttps = $true
    if ($CertPath) {
        $installParams.CertPath = $CertPath
    }
    if ($CertPassword) {
        $installParams.CertPassword = $CertPassword
    }
}

try {
    # Call install.ps1 with parameters (it handles file copying, service creation, etc.)
    & $installScript @installParams
    if ($LASTEXITCODE -ne 0) {
        throw "Installation failed with exit code $LASTEXITCODE"
    }
    Write-Host "  Installation completed" -ForegroundColor Green
} catch {
    Write-Host "  Installation failed: $_" -ForegroundColor Red
    throw
}

# Step 4: Verify installation
Write-Host "[4/5] Verifying installation..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -eq "Running") {
    Write-Host "  Service is running" -ForegroundColor Green
} else {
    Write-Host "  Service status: $($service.Status)" -ForegroundColor Yellow
}

$portCheck = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($portCheck) {
    Write-Host "  Port $Port is listening" -ForegroundColor Green
} else {
    Write-Host "  Port $Port is NOT listening" -ForegroundColor Yellow
}

# Step 5: Test health endpoint
Write-Host "[5/5] Testing health endpoint..." -ForegroundColor Yellow
$protocol = if ($UseHttps) { "https" } else { "http" }
$healthUrl = "${protocol}://localhost:${Port}/api/v1/health"

$maxRetries = 10
$retryCount = 0
$healthOk = $false

while ($retryCount -lt $maxRetries -and -not $healthOk) {
    Start-Sleep -Seconds 2
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            $healthOk = $true
            Write-Host "  Health check passed (Status: $($response.StatusCode))" -ForegroundColor Green
            $response.Content | Out-Null
        }
    } catch {
        $retryCount++
        if ($retryCount -lt $maxRetries) {
            Write-Host "  Health check failed, retrying ($retryCount/$maxRetries)..." -ForegroundColor Yellow
        }
    }
}

if (-not $healthOk) {
    Write-Host "  Health endpoint not responding after $maxRetries retries" -ForegroundColor Red
    Write-Host ""
    Write-Host "Recent Event Log errors:" -ForegroundColor Yellow
    Get-EventLog -LogName Application -Source $serviceName -EntryType Error -Newest 5 -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  [$($_.TimeGenerated)] $($_.Message.Substring(0, [Math]::Min(200, $_.Message.Length)))" -ForegroundColor Red
    }
} else {
    Write-Host ""
    Write-Host "=== Setup Complete ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installation Details:" -ForegroundColor Cyan
    Write-Host "  Service: $serviceName" -ForegroundColor White
    Write-Host "  Port: $Port" -ForegroundColor White
    Write-Host "  Health URL: $healthUrl" -ForegroundColor White
    Write-Host "  UI URL: ${protocol}://localhost:${Port}/ui" -ForegroundColor White
    Write-Host ""
    Write-Host "Setup folder is now portable:" -ForegroundColor Cyan
    Write-Host "  Binaries: $publishPath" -ForegroundColor White
    Write-Host "  Install script: $installScript" -ForegroundColor White
    Write-Host "  Uninstall script: $(Join-Path $scriptDir 'uninstall.ps1')" -ForegroundColor White
    Write-Host ""
    Write-Host "You can distribute the 'setup' folder independently." -ForegroundColor Gray
}

exit 0

