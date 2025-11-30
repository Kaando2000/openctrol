# Diagnostic script to check MSI custom action DLL
param([string]$MsiPath = "dist\OpenctrolAgentSetup.msi")

Write-Host "=== MSI Custom Action DLL Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $MsiPath)) {
    Write-Error "MSI not found: $MsiPath"
    exit 1
}

$msi = Get-Item $MsiPath
Write-Host "MSI: $($msi.FullName)" -ForegroundColor Cyan
Write-Host "Size: $([math]::Round($msi.Length / 1MB, 2)) MB" -ForegroundColor Cyan
Write-Host ""

# Check if CustomActions DLL exists
$dllPath = "installer\Openctrol.Agent.Setup\CustomActions\bin\Release\net8.0-windows\CustomActions.dll"
if (Test-Path $dllPath) {
    $dll = Get-Item $dllPath
    Write-Host " CustomActions.dll found" -ForegroundColor Green
    Write-Host "  Path: $($dll.FullName)" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($dll.Length / 1KB, 2)) KB" -ForegroundColor Gray
} else {
    Write-Host " CustomActions.dll NOT found" -ForegroundColor Red
}

Write-Host ""
Write-Host "To diagnose the installation error:" -ForegroundColor Yellow
Write-Host "1. Run: msiexec /i $MsiPath /l*v install.log" -ForegroundColor White
Write-Host "2. Check install.log for 'CustomActions' or 'Binary' entries" -ForegroundColor White
Write-Host "3. Look for error messages about DLL loading" -ForegroundColor White
