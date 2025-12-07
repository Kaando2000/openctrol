# Fix Openctrol Agent Permissions Script
# Requires Administrator privileges

param()

$ErrorActionPreference = "Stop"

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Elevate if not admin
if (-not (Test-Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    $scriptPath = $MyInvocation.MyCommand.Path
    $argList = "-ExecutionPolicy Bypass -File `"$scriptPath`""
    
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait
    exit $LASTEXITCODE
}

Write-Host "=== Fixing Openctrol Agent Permissions ===" -ForegroundColor Cyan
Write-Host ""

$configPath = "C:\ProgramData\Openctrol"
$logsPath = "C:\ProgramData\Openctrol\logs"

try {
    # Ensure config directory exists
    if (-not (Test-Path $configPath)) {
        New-Item -ItemType Directory -Path $configPath -Force | Out-Null
        Write-Host "Created config directory: $configPath" -ForegroundColor Green
    } else {
        Write-Host "Config directory already exists: $configPath" -ForegroundColor Gray
    }
    
    # Ensure logs directory exists - remove and recreate if it has wrong permissions
    if (Test-Path $logsPath) {
        Write-Host "Logs directory exists, checking permissions..." -ForegroundColor Yellow
        try {
            $testFile = Join-Path $logsPath "test-write.tmp"
            "test" | Out-File -FilePath $testFile -ErrorAction Stop
            Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            Write-Host "Logs directory is writable" -ForegroundColor Green
        } catch {
            Write-Host "Logs directory has permission issues, removing and recreating..." -ForegroundColor Yellow
            try {
                Get-ChildItem -Path $logsPath -Force -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
                Remove-Item -Path $logsPath -Force -ErrorAction SilentlyContinue
            } catch {
                Write-Warning "Could not remove existing logs directory: $_"
            }
            New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
            Write-Host "Recreated logs directory: $logsPath" -ForegroundColor Green
        }
    } else {
        New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
        Write-Host "Created logs directory: $logsPath" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Setting permissions on config directory..." -ForegroundColor Yellow
    
    $result = icacls $configPath /grant "SYSTEM:(OI)(CI)F" /grant "Administrators:(OI)(CI)F" /inheritance:r 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Config directory permissions set" -ForegroundColor Green
    } else {
        Write-Warning "Failed to set config directory permissions"
        Write-Host $result
    }
    
    Write-Host "Setting permissions on logs directory..." -ForegroundColor Yellow
    $result = icacls $logsPath /grant "SYSTEM:(OI)(CI)F" /grant "Administrators:(OI)(CI)F" /inheritance:r 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Logs directory permissions set" -ForegroundColor Green
    } else {
        Write-Warning "Failed to set logs directory permissions"
        Write-Host $result
    }
    
    Write-Host ""
    Write-Host "Verifying permissions..." -ForegroundColor Yellow
    $configAcl = Get-Acl $configPath
    $logsAcl = Get-Acl $logsPath
    
    Write-Host "Config directory access:" -ForegroundColor Cyan
    $configAcl.Access | Where-Object { $_.IdentityReference -like "*SYSTEM*" -or $_.IdentityReference -like "*Administrators*" } | Format-Table IdentityReference, FileSystemRights, AccessControlType -AutoSize
    
    Write-Host "Logs directory access:" -ForegroundColor Cyan
    $logsAcl.Access | Where-Object { $_.IdentityReference -like "*SYSTEM*" -or $_.IdentityReference -like "*Administrators*" } | Format-Table IdentityReference, FileSystemRights, AccessControlType -AutoSize
    
    Write-Host ""
    Write-Host "=== Permissions Fixed ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now restart the service:" -ForegroundColor Cyan
    Write-Host "  Restart-Service -Name OpenctrolAgent" -ForegroundColor White
    
} catch {
    Write-Error "Failed to fix permissions: $_"
    exit 1
}

