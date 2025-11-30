# Openctrol Agent Uninstaller Script
# Location: setup/uninstall.ps1
# Removes Openctrol Agent Windows service and files
# Requires Administrator privileges

<#
.SYNOPSIS
    Uninstalls Openctrol Agent and removes all components.

.DESCRIPTION
    This script uninstalls the Openctrol Agent by:
    - Stopping and deleting the Windows service
    - Removing firewall rules
    - Removing installation files
    - Optionally removing configuration and logs

.PARAMETER RemoveProgramData
    Remove configuration and logs from C:\ProgramData\Openctrol (default: $false)

.PARAMETER SkipFirewallCleanup
    Skip removal of firewall rules (default: $false)

.PARAMETER InstallPath
    Installation directory to remove (default: C:\Program Files\Openctrol)

.EXAMPLE
    .\uninstall-openctrol-agent.ps1

.EXAMPLE
    .\uninstall-openctrol-agent.ps1 -RemoveProgramData

.EXAMPLE
    .\uninstall-openctrol-agent.ps1 -RemoveProgramData -SkipFirewallCleanup
#>

param(
    [switch]$RemoveProgramData = $false,
    [switch]$SkipFirewallCleanup = $false,
    [string]$InstallPath = "C:\Program Files\Openctrol"
)

$ErrorActionPreference = "Stop"

# Service configuration
$ServiceName = "OpenctrolAgent"
$ConfigPath = "C:\ProgramData\Openctrol"

# Check for admin privileges
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run PowerShell as Administrator."
    Write-Host "Right-click PowerShell and select 'Run as Administrator', then run this script again." -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Openctrol Agent Uninstaller ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Stop and delete service
Write-Host "[1/4] Removing Windows Service..." -ForegroundColor Yellow
try {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    
    if ($service) {
        if ($service.Status -eq "Running") {
            Write-Host "  Stopping service..." -ForegroundColor Gray
            Stop-Service -Name $ServiceName -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
            Write-Host "  Service stopped" -ForegroundColor Green
        }
        
        Write-Host "  Deleting service..." -ForegroundColor Gray
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        
        # Verify deletion
        $verifyService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $verifyService) {
            Write-Host "  Service deleted successfully" -ForegroundColor Green
        } else {
            Write-Warning "Service may still exist. You may need to reboot to complete removal."
        }
    } else {
        Write-Host "  Service does not exist, skipping" -ForegroundColor Gray
    }
} catch {
    Write-Warning "Error removing service: $_"
    Write-Warning "Service may need to be removed manually"
}

# Step 2: Remove firewall rule
if (-not $SkipFirewallCleanup) {
    Write-Host "[2/4] Removing Firewall Rule..." -ForegroundColor Yellow
    try {
        $firewallRuleName = "Openctrol Agent"
        $rule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
        
        if ($rule) {
            Remove-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction Stop
            Write-Host "  Removed firewall rule: $firewallRuleName" -ForegroundColor Green
        } else {
            Write-Host "  Firewall rule does not exist, skipping" -ForegroundColor Gray
        }
    } catch {
        Write-Warning "Failed to remove firewall rule: $_"
        Write-Warning "You may need to remove it manually from Windows Firewall"
    }
} else {
    Write-Host "[2/4] Skipping firewall cleanup (SkipFirewallCleanup = true)" -ForegroundColor Yellow
}

# Step 3: Remove installation directory
Write-Host "[3/4] Removing Installation Files..." -ForegroundColor Yellow
try {
    if (Test-Path $InstallPath) {
        Write-Host "  Removing: $InstallPath" -ForegroundColor Gray
        Remove-Item -Path $InstallPath -Recurse -Force -ErrorAction Stop
        Write-Host "  Installation files removed" -ForegroundColor Green
    } else {
        Write-Host "  Installation directory does not exist, skipping" -ForegroundColor Gray
    }
} catch {
    Write-Warning "Failed to remove installation directory: $_"
    Write-Warning "You may need to remove it manually: $InstallPath"
}

# Step 4: Remove ProgramData (optional)
if ($RemoveProgramData) {
    Write-Host "[4/4] Removing Configuration and Logs..." -ForegroundColor Yellow
    try {
        if (Test-Path $ConfigPath) {
            Write-Host "  Removing: $ConfigPath" -ForegroundColor Gray
            Remove-Item -Path $ConfigPath -Recurse -Force -ErrorAction Stop
            Write-Host "  Configuration and logs removed" -ForegroundColor Green
        } else {
            Write-Host "  Configuration directory does not exist, skipping" -ForegroundColor Gray
        }
    } catch {
        Write-Warning "Failed to remove configuration directory: $_"
        Write-Warning "You may need to remove it manually: $ConfigPath"
    }
} else {
    Write-Host "[4/4] Preserving Configuration and Logs..." -ForegroundColor Yellow
    Write-Host "  Configuration preserved at: $ConfigPath" -ForegroundColor Gray
    Write-Host "  Use -RemoveProgramData to delete configuration and logs" -ForegroundColor Gray
}

# Step 5: Remove Event Log source (optional cleanup)
Write-Host ""
Write-Host "Cleaning up Event Log source..." -ForegroundColor Yellow
try {
    if ([System.Diagnostics.EventLog]::SourceExists("OpenctrolAgent")) {
        Remove-EventLog -Source "OpenctrolAgent" -ErrorAction Stop
        Write-Host "  Removed Event Log source" -ForegroundColor Green
    } else {
        Write-Host "  Event Log source does not exist, skipping" -ForegroundColor Gray
    }
} catch {
    Write-Warning "Failed to remove Event Log source: $_"
    Write-Warning "This is non-critical and can be ignored"
}

# Success summary
Write-Host ""
Write-Host "=== Uninstallation Complete ===" -ForegroundColor Green
Write-Host ""

if ($RemoveProgramData) {
    Write-Host "All components have been removed:" -ForegroundColor Cyan
    Write-Host "  ✓ Windows Service" -ForegroundColor White
    Write-Host "  ✓ Firewall Rules" -ForegroundColor White
    Write-Host "  ✓ Installation Files" -ForegroundColor White
    Write-Host "  ✓ Configuration and Logs" -ForegroundColor White
} else {
    Write-Host "Components removed:" -ForegroundColor Cyan
    Write-Host "  ✓ Windows Service" -ForegroundColor White
    Write-Host "  ✓ Firewall Rules" -ForegroundColor White
    Write-Host "  ✓ Installation Files" -ForegroundColor White
    Write-Host ""
    Write-Host "Preserved (for reinstallation):" -ForegroundColor Cyan
    Write-Host "  - Configuration: $ConfigPath" -ForegroundColor White
    $logsPath = Join-Path $ConfigPath "logs"
    Write-Host "  - Logs: $logsPath" -ForegroundColor White
}

Write-Host ""

exit 0

