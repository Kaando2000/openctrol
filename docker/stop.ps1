# PowerShell script to stop the Home Assistant test server
# Usage: .\docker\stop.ps1

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Stopping Home Assistant Test Server" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to docker directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$dockerDir = Join-Path $scriptPath "."
Push-Location $dockerDir

try {
    # Check if container exists
    $containerExists = docker ps -a --filter "name=openctrol-ha-test" --format "{{.Names}}"
    
    if (-not $containerExists) {
        Write-Host "  ⚠ Container 'openctrol-ha-test' not found" -ForegroundColor Yellow
        Write-Host "  Nothing to stop." -ForegroundColor Gray
        exit 0
    }
    
    # Check if container is running
    $containerRunning = docker ps --filter "name=openctrol-ha-test" --format "{{.Names}}"
    
    if (-not $containerRunning) {
        Write-Host "  ⚠ Container is not running" -ForegroundColor Yellow
        Write-Host "  Container exists but is stopped." -ForegroundColor Gray
        exit 0
    }
    
    Write-Host "Stopping container..." -ForegroundColor Yellow
    
    # Stop the container gracefully
    docker compose down
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ Failed to stop container" -ForegroundColor Red
        Write-Host ""
        Write-Host "Try manually: docker stop openctrol-ha-test" -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host "  ✓ Container stopped successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "To start again: .\docker\start.ps1" -ForegroundColor Gray
    Write-Host ""
    
} catch {
    Write-Host "  ✗ Error stopping container: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

