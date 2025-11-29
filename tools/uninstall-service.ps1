# Openctrol Agent Service Uninstallation Script
# Requires Administrator privileges

param(
    [string]$ServiceName = "OpenctrolAgent"
)

$ErrorActionPreference = "Stop"

# Check for admin privileges
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' does not exist." -ForegroundColor Yellow
    exit 0
}

# Stop the service if running
if ($service.Status -eq "Running") {
    Write-Host "Stopping service..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

# Remove the service
Write-Host "Removing service..." -ForegroundColor Cyan
sc.exe delete $ServiceName
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to remove service"
    exit 1
}

Start-Sleep -Seconds 2

# Remove Event Log source
Write-Host "Removing Event Log source..." -ForegroundColor Cyan
if ([System.Diagnostics.EventLog]::SourceExists("OpenctrolAgent")) {
    Remove-EventLog -Source "OpenctrolAgent" -ErrorAction SilentlyContinue
    Write-Host "Event Log source removed." -ForegroundColor Green
} else {
    Write-Host "Event Log source does not exist." -ForegroundColor Yellow
}

Write-Host "Service uninstalled successfully!" -ForegroundColor Green

