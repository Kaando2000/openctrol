# Enhanced MSI Diagnostic Script
param([string]$MsiPath = "dist\OpenctrolAgentSetup.msi")

Write-Host "=== MSI Custom Action Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $MsiPath)) {
    Write-Error "MSI not found: $MsiPath"
    exit 1
}

$msi = Get-Item $MsiPath
Write-Host "MSI: $($msi.FullName)" -ForegroundColor Cyan
Write-Host "Size: $([math]::Round($msi.Length / 1MB, 2)) MB" -ForegroundColor Cyan
Write-Host ""

# Check DLL
$dllPath = "installer\Openctrol.Agent.Setup\CustomActions\bin\Release\net8.0-windows\CustomActions.dll"
if (Test-Path $dllPath) {
    $dll = Get-Item $dllPath
    Write-Host " CustomActions.dll found" -ForegroundColor Green
    Write-Host "  Path: $($dll.FullName)" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($dll.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host "  Modified: $($dll.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host " CustomActions.dll NOT found" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Install with logging:" -ForegroundColor White
Write-Host "   msiexec /i $MsiPath /l*v install.log" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Check install.log for:" -ForegroundColor White
Write-Host "   - 'CustomActionsBinary' (should appear in Binary table)" -ForegroundColor Cyan
Write-Host "   - 'CustomAction' errors" -ForegroundColor Cyan
Write-Host "   - 'DLL' loading errors" -ForegroundColor Cyan
Write-Host ""
Write-Host "3. Common issues:" -ForegroundColor White
Write-Host "   - DLL not in Binary table = WiX variable not resolved" -ForegroundColor Yellow
Write-Host "   - 'Cannot load DLL' = Architecture mismatch or missing dependency" -ForegroundColor Yellow
Write-Host "   - 'Entry point not found' = Method signature mismatch" -ForegroundColor Yellow
