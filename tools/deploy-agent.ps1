# Deploy Openctrol Agent
# Run this script as Administrator

$ErrorActionPreference = "Stop"

Write-Host "Deploying Openctrol Agent..." -ForegroundColor Green

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator" -ForegroundColor Red
    exit 1
}

$serviceName = "OpenctrolAgent"
$installPath = "C:\Program Files\Openctrol"
$sourcePath = "$PSScriptRoot\..\src\Openctrol.Agent\bin\Release\net8.0-windows"

# Check if service exists
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $serviceName -Force
    Start-Sleep -Seconds 2
}

# Check if source files exist
if (-not (Test-Path $sourcePath)) {
    Write-Host "ERROR: Source files not found at $sourcePath" -ForegroundColor Red
    Write-Host "Please build the agent first: dotnet build -c Release" -ForegroundColor Yellow
    exit 1
}

# Copy files
Write-Host "Copying files to $installPath..." -ForegroundColor Yellow
if (-not (Test-Path $installPath)) {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
}

Copy-Item -Path "$sourcePath\*" -Destination $installPath -Force -Recurse -ErrorAction Stop

Write-Host "Files copied successfully" -ForegroundColor Green

# Start service
if ($service) {
    Write-Host "Starting service..." -ForegroundColor Yellow
    Start-Service -Name $serviceName
    Start-Sleep -Seconds 2
    
    $serviceStatus = Get-Service -Name $serviceName
    if ($serviceStatus.Status -eq "Running") {
        Write-Host "Service started successfully" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Service status is $($serviceStatus.Status)" -ForegroundColor Yellow
    }
}

Write-Host "Deployment complete!" -ForegroundColor Green

