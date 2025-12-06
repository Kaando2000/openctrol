# PowerShell script to start the Home Assistant test server
# Usage: .\docker\start.ps1

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Openctrol Home Assistant Test Server" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is installed and running
Write-Host "Checking Docker installation..." -ForegroundColor Yellow
try {
    $dockerVersion = docker --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Docker not found"
    }
    Write-Host "  ✓ Docker found: $dockerVersion" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Docker is not installed or not in PATH" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Docker Desktop from:" -ForegroundColor Yellow
    Write-Host "  https://www.docker.com/products/docker-desktop" -ForegroundColor Cyan
    exit 1
}

# Check if Docker daemon is running
Write-Host "Checking Docker daemon..." -ForegroundColor Yellow
try {
    docker ps | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker daemon not running"
    }
    Write-Host "  ✓ Docker daemon is running" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Docker daemon is not running" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please start Docker Desktop and try again." -ForegroundColor Yellow
    exit 1
}

# Check if docker-compose is available
Write-Host "Checking Docker Compose..." -ForegroundColor Yellow
try {
    $composeVersion = docker compose version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose not found"
    }
    Write-Host "  ✓ Docker Compose found: $composeVersion" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Docker Compose not found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Docker Compose should be included with Docker Desktop." -ForegroundColor Yellow
    Write-Host "Please ensure Docker Desktop is up to date." -ForegroundColor Yellow
    exit 1
}

# Navigate to docker directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$dockerDir = Join-Path $scriptPath "."
Push-Location $dockerDir

try {
    Write-Host ""
    Write-Host "Starting Home Assistant container..." -ForegroundColor Yellow
    
    # Check if container is already running
    $existingContainer = docker ps -a --filter "name=openctrol-ha-test" --format "{{.Names}} {{.Status}}"
    if ($existingContainer -match "Up") {
        Write-Host "  ⚠ Container is already running" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Access Home Assistant at: http://localhost:8123" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "To view logs: docker-compose logs -f homeassistant" -ForegroundColor Gray
        Write-Host "To stop: .\docker\stop.ps1" -ForegroundColor Gray
        exit 0
    }
    
    # Start the container
    docker compose up -d
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ Failed to start container" -ForegroundColor Red
        Write-Host ""
        Write-Host "Check the error messages above for details." -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host "  ✓ Container started successfully" -ForegroundColor Green
    Write-Host ""
    
    # Wait a moment for container to initialize
    Write-Host "Waiting for Home Assistant to initialize..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    
    # Check container status
    $containerStatus = docker ps --filter "name=openctrol-ha-test" --format "{{.Status}}"
    if ($containerStatus) {
        Write-Host "  ✓ Container is running: $containerStatus" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ Container may not be running properly" -ForegroundColor Yellow
        Write-Host "  Check logs: docker-compose logs homeassistant" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Home Assistant Test Server is Ready!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Access Home Assistant at:" -ForegroundColor White
    Write-Host "  http://localhost:8123" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Useful commands:" -ForegroundColor White
    Write-Host "  View logs:     docker-compose logs -f homeassistant" -ForegroundColor Gray
    Write-Host "  Stop server:   .\docker\stop.ps1" -ForegroundColor Gray
    Write-Host "  Container shell: docker exec -it openctrol-ha-test bash" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor White
    Write-Host "  1. Open http://localhost:8123 in your browser" -ForegroundColor Gray
    Write-Host "  2. Complete the setup wizard" -ForegroundColor Gray
    Write-Host "  3. Configure Openctrol integration:" -ForegroundColor Gray
    Write-Host "     Settings → Devices & Services → Add Integration" -ForegroundColor Gray
    Write-Host "  4. Use 'host.docker.internal' as the agent host" -ForegroundColor Gray
    Write-Host ""
    
} finally {
    Pop-Location
}

