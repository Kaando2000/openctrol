# Openctrol Agent Service Installation Script
# Requires Administrator privileges

param(
    [string]$ServiceName = "OpenctrolAgent",
    [string]$DisplayName = "Openctrol Agent",
    [string]$Description = "Openctrol Windows Remote Desktop Agent",
    [string]$ServiceAccount = "LocalSystem"
)

$ErrorActionPreference = "Stop"

# Check for admin privileges
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$serviceExe = Join-Path $projectRoot "src\Openctrol.Agent\bin\Release\net8.0-windows\Openctrol.Agent.exe"

# Build the service
Write-Host "Building Openctrol Agent..." -ForegroundColor Cyan
Push-Location $projectRoot
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $serviceExe)) {
    Write-Error "Service executable not found at: $serviceExe"
    exit 1
}

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow
    if ($existingService.Status -eq "Running") {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create Event Log source
Write-Host "Creating Event Log source..." -ForegroundColor Cyan
if (-not [System.Diagnostics.EventLog]::SourceExists("OpenctrolAgent")) {
    New-EventLog -LogName Application -Source "OpenctrolAgent"
    Write-Host "Event Log source created." -ForegroundColor Green
} else {
    Write-Host "Event Log source already exists." -ForegroundColor Yellow
}

# Create ProgramData directories
Write-Host "Creating ProgramData directories..." -ForegroundColor Cyan
$programData = $env:ProgramData
$openctrolDir = Join-Path $programData "Openctrol"
$logsDir = Join-Path $openctrolDir "logs"

New-Item -ItemType Directory -Force -Path $openctrolDir | Out-Null
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
Write-Host "Directories created." -ForegroundColor Green

# Install the service
Write-Host "Installing service..." -ForegroundColor Cyan
$binPath = "`"$serviceExe`""

sc.exe create $ServiceName binPath= $binPath DisplayName= $DisplayName start= auto
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service"
    exit 1
}

# Set service description
sc.exe description $ServiceName $Description

# Configure service recovery (restart on failure)
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Set service account
if ($ServiceAccount -eq "LocalSystem") {
    sc.exe config $ServiceName obj= "LocalSystem"
} else {
    sc.exe config $ServiceName obj= $ServiceAccount
}

Write-Host "Service installed successfully!" -ForegroundColor Green
Write-Host "Service Name: $ServiceName" -ForegroundColor Cyan
Write-Host "Display Name: $DisplayName" -ForegroundColor Cyan
Write-Host "Executable: $serviceExe" -ForegroundColor Cyan
Write-Host ""
Write-Host "To start the service, run: Start-Service -Name $ServiceName" -ForegroundColor Yellow

