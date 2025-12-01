# Test script for Openctrol Agent API
# Usage: .\test-openctrol-api.ps1 -Host localhost -Port 44325 -ApiKey "your-api-key"

param(
    [string]$Host = "localhost",
    [int]$Port = 44325,
    [string]$ApiKey = ""
)

$baseUrl = "http://${Host}:${Port}"
$headers = @{}

if ($ApiKey) {
    $headers["X-Openctrol-Key"] = $ApiKey
    Write-Host "Using API key authentication" -ForegroundColor Green
} else {
    Write-Host "WARNING: No API key provided. Some endpoints may fail." -ForegroundColor Yellow
}

Write-Host "`n=== Openctrol Agent API Test ===" -ForegroundColor Cyan
Write-Host "Base URL: $baseUrl`n" -ForegroundColor Cyan

# Test 1: Health endpoint
Write-Host "1. Testing GET /api/v1/health..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/health" -Method Get
    Write-Host "   ✓ Health check successful" -ForegroundColor Green
    Write-Host "   Agent ID: $($response.agent_id)" -ForegroundColor Gray
    Write-Host "   Version: $($response.version)" -ForegroundColor Gray
    Write-Host "   Uptime: $($response.uptime_seconds) seconds" -ForegroundColor Gray
    Write-Host "   Active Sessions: $($response.active_sessions)" -ForegroundColor Gray
    $response | ConvertTo-Json -Depth 3 | Write-Host
} catch {
    Write-Host "   ✗ Health check failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n"

# Test 2: Audio status
Write-Host "2. Testing GET /api/v1/audio/status..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/audio/status" -Method Get -Headers $headers
    Write-Host "   ✓ Audio status retrieved" -ForegroundColor Green
    Write-Host "   Master Volume: $($response.master.volume)%, Muted: $($response.master.muted)" -ForegroundColor Gray
    Write-Host "   Devices: $($response.devices.Count)" -ForegroundColor Gray
    foreach ($device in $response.devices) {
        $default = if ($device.is_default) { " (DEFAULT)" } else { "" }
        Write-Host "     - $($device.name)$default : $($device.volume)%, Muted: $($device.muted)" -ForegroundColor Gray
    }
    $response | ConvertTo-Json -Depth 3 | Write-Host
} catch {
    Write-Host "   ✗ Audio status failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "   (Authentication required)" -ForegroundColor Yellow
    }
}

Write-Host "`n"

# Test 3: Audio master volume (dry run - show what would be sent)
Write-Host "3. Testing POST /api/v1/audio/master (DRY RUN)..." -ForegroundColor Yellow
Write-Host "   This would set master volume to 75% (commented out to avoid changes)" -ForegroundColor Gray
Write-Host "   Uncomment the code below to actually test:" -ForegroundColor Gray
Write-Host "   `$body = @{ volume = 75; muted = `$false } | ConvertTo-Json" -ForegroundColor DarkGray
Write-Host "   `$response = Invoke-RestMethod -Uri `"$baseUrl/api/v1/audio/master`" -Method Post -Body `$body -Headers `$headers -ContentType 'application/json'" -ForegroundColor DarkGray

# Uncomment to actually test:
# try {
#     $body = @{
#         volume = 75
#         muted = $false
#     } | ConvertTo-Json
#     $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/audio/master" -Method Post -Body $body -Headers $headers -ContentType "application/json"
#     Write-Host "   ✓ Master volume set" -ForegroundColor Green
#     $response | ConvertTo-Json | Write-Host
# } catch {
#     Write-Host "   ✗ Master volume set failed: $($_.Exception.Message)" -ForegroundColor Red
# }

Write-Host "`n"

# Test 4: Monitors
Write-Host "4. Testing GET /api/v1/rd/monitors..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/rd/monitors" -Method Get -Headers $headers
    Write-Host "   ✓ Monitors enumeration successful" -ForegroundColor Green
    Write-Host "   Current Monitor: $($response.current_monitor_id)" -ForegroundColor Gray
    Write-Host "   Available Monitors: $($response.monitors.Count)" -ForegroundColor Gray
    foreach ($monitor in $response.monitors) {
        $primary = if ($monitor.is_primary) { " (PRIMARY)" } else { "" }
        Write-Host "     - $($monitor.id)$primary : $($monitor.resolution) - $($monitor.name)" -ForegroundColor Gray
    }
    $response | ConvertTo-Json -Depth 3 | Write-Host
} catch {
    Write-Host "   ✗ Monitors enumeration failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "   (Authentication required)" -ForegroundColor Yellow
    }
}

Write-Host "`n"

# Test 5: Power endpoint (dry run - show what would be sent)
Write-Host "5. Testing POST /api/v1/power (DRY RUN - COMMENTED OUT FOR SAFETY)..." -ForegroundColor Yellow
Write-Host "   WARNING: Power actions will restart or shutdown the system!" -ForegroundColor Red
Write-Host "   This test is commented out to prevent accidental system restart/shutdown." -ForegroundColor Yellow
Write-Host "   To test power endpoint, uncomment the code below:" -ForegroundColor Gray
Write-Host "   `$body = @{ action = 'restart'; force = `$false } | ConvertTo-Json" -ForegroundColor DarkGray
Write-Host "   `$response = Invoke-RestMethod -Uri `"$baseUrl/api/v1/power`" -Method Post -Body `$body -Headers `$headers -ContentType 'application/json'" -ForegroundColor DarkGray

# DO NOT UNCOMMENT UNLESS YOU WANT TO RESTART/SHUTDOWN THE SYSTEM:
# try {
#     $body = @{
#         action = "restart"
#         force = $false
#     } | ConvertTo-Json
#     $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/power" -Method Post -Body $body -Headers $headers -ContentType "application/json"
#     Write-Host "   ✓ Power action sent (system may restart)" -ForegroundColor Green
#     $response | ConvertTo-Json | Write-Host
# } catch {
#     Write-Host "   ✗ Power action failed: $($_.Exception.Message)" -ForegroundColor Red
# }

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
Write-Host "`nNote: WebSocket endpoint (/api/v1/rd/session) requires a WebSocket client to test." -ForegroundColor Gray
Write-Host "Use a tool like Postman, wscat, or a custom script to test WebSocket connections." -ForegroundColor Gray

