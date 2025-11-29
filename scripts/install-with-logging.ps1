# Install Openctrol Agent with verbose logging
# This script runs the MSI installer with full logging enabled for troubleshooting

param(
    [Parameter(Mandatory=$false)]
    [string]$MsiPath = "dist\OpenctrolAgentSetup.msi",
    
    [Parameter(Mandatory=$false)]
    [string]$LogPath = "install.log"
)

if (-not (Test-Path $MsiPath)) {
    Write-Error "MSI file not found at: $MsiPath"
    Write-Host "Please build the installer first using: .\scripts\build-installer.ps1"
    exit 1
}

Write-Host "Installing Openctrol Agent with verbose logging..." -ForegroundColor Yellow
Write-Host "MSI: $MsiPath" -ForegroundColor Cyan
Write-Host "Log: $LogPath" -ForegroundColor Cyan
Write-Host ""

# Run installer with verbose logging
$logPathFull = (Resolve-Path (Split-Path $MsiPath -Parent)).Path + "\" + $LogPath

Start-Process -FilePath "msiexec.exe" -ArgumentList @(
    "/i",
    "`"$((Resolve-Path $MsiPath).Path)`"",
    "/l*v",
    "`"$logPathFull`"",
    "/qb"  # Quiet with basic UI (change to /qn for completely silent)
) -Wait -NoNewWindow

Write-Host ""
Write-Host "Installation complete. Log file: $logPathFull" -ForegroundColor Green
Write-Host "Review the log file for any errors or warnings." -ForegroundColor Yellow

