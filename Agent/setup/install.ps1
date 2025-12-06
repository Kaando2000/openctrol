# Openctrol Agent Installation Script
# Location: setup/install.ps1
# Installs Openctrol Agent as a Windows service
# Requires Administrator privileges

<#
.SYNOPSIS
    Installs Openctrol Agent as a Windows service.

.DESCRIPTION
    This script installs the Openctrol Agent by:
    - Copying binaries to the installation folder
    - Creating configuration file
    - Installing and starting the Windows service
    - Optionally creating a firewall rule

.PARAMETER InstallPath
    Installation directory for binaries (default: C:\Program Files\Openctrol)

.PARAMETER ConfigPath
    Configuration directory (default: C:\ProgramData\Openctrol)

.PARAMETER Port
    HTTP port for the agent (default: 44325)

.PARAMETER UseHttps
    Enable HTTPS (requires certificate)

.PARAMETER CertPath
    Path to certificate file (required if UseHttps is true)

.PARAMETER CertPassword
    Certificate password (optional, will be encrypted with DPAPI)

.PARAMETER ApiKey
    API key for authentication (if not provided, will be generated)

.PARAMETER CreateFirewallRule
    Create Windows Firewall rule (default: $true)

.PARAMETER SourcePath
    Path to source binaries (default: current directory or .\bin subfolder)

.EXAMPLE
    .\install-openctrol-agent.ps1

.EXAMPLE
    .\install-openctrol-agent.ps1 -Port 8080 -ApiKey "my-secret-key"

.EXAMPLE
    .\install-openctrol-agent.ps1 -UseHttps -CertPath "C:\certs\cert.pfx" -CertPassword "password"
#>

param(
    [string]$InstallPath = "C:\Program Files\Openctrol",
    [string]$ConfigPath = "C:\ProgramData\Openctrol",
    [int]$Port = 44325,
    [switch]$UseHttps,
    [string]$CertPath = "",
    [string]$CertPassword = "",
    [string]$ApiKey = "",
    [bool]$CreateFirewallRule = $true,
    [string]$SourcePath = ""
)

$ErrorActionPreference = "Stop"

# Service configuration
$ServiceName = "OpenctrolAgent"
$ServiceDisplayName = "Openctrol Agent"
$ServiceDescription = "Provides remote control and desktop streaming for Home Assistant over the local network."

# Check for admin privileges
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Generate random bytes (compatible with PowerShell 5.x / .NET Framework)
function New-RandomBytes {
    param(
        [int]$Length
    )
    
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] $Length
        $rng.GetBytes($bytes)
        return $bytes
    }
    finally {
        if ($rng) {
            $rng.Dispose()
        }
    }
}

# Generate a random API key (compatible with PowerShell 5.x / .NET Framework)
function New-RandomApiKey {
    $bytes = New-RandomBytes -Length 32
    $base64 = [Convert]::ToBase64String($bytes)
    return $base64 -replace '\+', '-' -replace '/', '_' -replace '=', ''
}

if (-not (Test-Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run PowerShell as Administrator."
    Write-Host "Right-click PowerShell and select 'Run as Administrator', then run this script again." -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Openctrol Agent Installer ===" -ForegroundColor Cyan
Write-Host ""

# Determine source path
if ([string]::IsNullOrEmpty($SourcePath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Split-Path -Parent $scriptDir
    
    # Try to find published binaries
    $possiblePaths = @(
        (Join-Path $scriptDir "bin"),
        (Join-Path $projectRoot "src\Openctrol.Agent\bin\Release\net8.0-windows\win-x64\publish"),
        (Join-Path $projectRoot "src\Openctrol.Agent\bin\Release\net8.0-windows"),
        $scriptDir,
        $PSScriptRoot
    )
    
    $SourcePath = $null
    foreach ($path in $possiblePaths) {
        $agentExe = Join-Path $path "Openctrol.Agent.exe"
        if (Test-Path $agentExe) {
            $SourcePath = $path
            Write-Host "Found binaries at: $SourcePath" -ForegroundColor Green
            break
        }
    }
    
    if ([string]::IsNullOrEmpty($SourcePath)) {
        Write-Error "Could not find Openctrol.Agent.exe. Please specify -SourcePath parameter or ensure binaries are in the current directory or .\bin subfolder."
        exit 1
    }
}

$agentExe = Join-Path $SourcePath "Openctrol.Agent.exe"
if (-not (Test-Path $agentExe)) {
    Write-Error "Openctrol.Agent.exe not found at: $agentExe"
    exit 1
}

Write-Host "Source: $SourcePath" -ForegroundColor Gray
Write-Host "Install: $InstallPath" -ForegroundColor Gray
Write-Host "Config: $ConfigPath" -ForegroundColor Gray
Write-Host ""

# Step 1: Create installation directory
Write-Host "[1/7] Creating installation directory..." -ForegroundColor Yellow
try {
    if (-not (Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        Write-Host "  Created: $InstallPath" -ForegroundColor Green
    } else {
        Write-Host "  Already exists: $InstallPath" -ForegroundColor Gray
    }
} catch {
    Write-Error "Failed to create installation directory: $_"
    exit 1
}

# Step 2: Copy binaries
Write-Host "[2/7] Copying binaries..." -ForegroundColor Yellow
try {
    $filesToCopy = Get-ChildItem -Path $SourcePath -File
    $copiedCount = 0
    foreach ($file in $filesToCopy) {
        $destPath = Join-Path $InstallPath $file.Name
        Copy-Item -Path $file.FullName -Destination $destPath -Force
        $copiedCount++
    }
    Write-Host "  Copied $copiedCount files" -ForegroundColor Green
} catch {
    Write-Error "Failed to copy binaries: $_"
    exit 1
}

# Step 3: Ensure ProgramData directory exists with proper ACL
Write-Host "[3/7] Setting up configuration directory..." -ForegroundColor Yellow
try {
    if (-not (Test-Path $ConfigPath)) {
        New-Item -ItemType Directory -Path $ConfigPath -Force | Out-Null
        Write-Host "  Created: $ConfigPath" -ForegroundColor Green
    } else {
        Write-Host "  Already exists: $ConfigPath" -ForegroundColor Gray
    }
    
    # Set ACL: Administrators and SYSTEM only
    $acl = Get-Acl $ConfigPath
    $acl.SetAccessRuleProtection($true, $false) # Disable inheritance, remove inherited rules
    
    # Remove all existing access rules
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    
    # Add Administrators
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null),
        "FullControl",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.AddAccessRule($adminRule)
    
    # Add SYSTEM
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null),
        "FullControl",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.AddAccessRule($systemRule)
    
    Set-Acl -Path $ConfigPath -AclObject $acl
    Write-Host "  Set restrictive ACLs (Administrators + SYSTEM only)" -ForegroundColor Green
} catch {
    Write-Warning "Failed to set ACLs on config directory (continuing): $_"
}

# Step 4: Create or reuse config.json
Write-Host "[4/7] Configuring agent..." -ForegroundColor Yellow
$configFile = Join-Path $ConfigPath "config.json"

if (Test-Path $configFile) {
    Write-Host "  Existing config.json found, preserving it" -ForegroundColor Yellow
    Write-Host "  Location: $configFile" -ForegroundColor Gray
} else {
    Write-Host "  Creating new config.json..." -ForegroundColor Gray
    
    # Generate Agent ID
    $agentId = [System.Guid]::NewGuid().ToString()
    
    # Generate API key if not provided
    if ([string]::IsNullOrEmpty($ApiKey)) {
        $ApiKey = New-RandomApiKey
        Write-Host "  Generated API key" -ForegroundColor Gray
    }
    
    # Encrypt certificate password if provided
    $certPasswordEncrypted = ""
    if ($UseHttps -and -not [string]::IsNullOrEmpty($CertPassword)) {
        try {
            Add-Type -AssemblyName System.Security
            $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($CertPassword)
            $encryptedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
                $plainBytes,
                $null,
                [System.Security.Cryptography.DataProtectionScope]::LocalMachine
            )
            $certPasswordEncrypted = [Convert]::ToBase64String($encryptedBytes)
            Write-Host "  Encrypted certificate password" -ForegroundColor Gray
        } catch {
            Write-Warning "Failed to encrypt certificate password: $_"
            Write-Warning "Certificate password will be stored unencrypted (not recommended)"
            $certPasswordEncrypted = $CertPassword
        }
    }
    
    # Validate HTTPS configuration
    if ($UseHttps) {
        if ([string]::IsNullOrEmpty($CertPath)) {
            Write-Error "CertPath is required when UseHttps is enabled"
            exit 1
        }
        if (-not (Test-Path $CertPath)) {
            Write-Error "Certificate file not found: $CertPath"
            exit 1
        }
    }
    
    # Create config object
    $config = @{
        AgentId = $agentId
        HttpPort = $Port
        MaxSessions = 1
        CertPath = if ($UseHttps) { $CertPath } else { "" }
        CertPasswordEncrypted = $certPasswordEncrypted
        TargetFps = 30
        AllowedHaIds = @()
        ApiKey = $ApiKey
    }
    
    # Write config file
    try {
        $json = $config | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($configFile, $json, [System.Text.Encoding]::UTF8)
        
        # Set restrictive file permissions
        try {
            $fileInfo = Get-Item $configFile
            $fileAcl = Get-Acl $configFile
            $fileAcl.SetAccessRuleProtection($true, $false)
            $fileAcl.Access | ForEach-Object { $fileAcl.RemoveAccessRule($_) | Out-Null }
            
            $adminFileRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null),
                "FullControl",
                "Allow"
            )
            $fileAcl.AddAccessRule($adminFileRule)
            
            $systemFileRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null),
                "FullControl",
                "Allow"
            )
            $fileAcl.AddAccessRule($systemFileRule)
            
            Set-Acl -Path $configFile -AclObject $fileAcl
        } catch {
            Write-Warning "Failed to set file permissions (continuing): $_"
        }
        
        Write-Host "  Created config.json" -ForegroundColor Green
        Write-Host "  Agent ID: $agentId" -ForegroundColor Gray
        Write-Host "  Port: $Port" -ForegroundColor Gray
        Write-Host "  API Key: $(if ([string]::IsNullOrEmpty($ApiKey)) { '(not set)' } else { 'configured' })" -ForegroundColor Gray
    } catch {
        Write-Error "Failed to create config file: $_"
        exit 1
    }
}

# Step 5: Create Event Log source
Write-Host "[5/7] Setting up Event Log..." -ForegroundColor Yellow
try {
    if (-not [System.Diagnostics.EventLog]::SourceExists("OpenctrolAgent")) {
        New-EventLog -LogName Application -Source "OpenctrolAgent" -ErrorAction Stop
        Write-Host "  Created Event Log source" -ForegroundColor Green
    } else {
        Write-Host "  Event Log source already exists" -ForegroundColor Gray
    }
} catch {
    Write-Warning "Failed to create Event Log source (continuing): $_"
}

# Step 6: Install/Update Windows Service
Write-Host "[6/7] Installing Windows Service..." -ForegroundColor Yellow
$serviceExe = Join-Path $InstallPath "Openctrol.Agent.exe"
$binPath = "`"$serviceExe`""

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($existingService) {
    Write-Host "  Service already exists, updating..." -ForegroundColor Yellow
    
    # Stop service if running
    if ($existingService.Status -eq "Running") {
        Write-Host "  Stopping service..." -ForegroundColor Gray
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Start-Sleep -Seconds 2
    }
    
    # Update service binary path
    sc.exe config $ServiceName binPath= $binPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to update service configuration"
        exit 1
    }
    Write-Host "  Updated service binary path" -ForegroundColor Green
} else {
    # Create new service
    sc.exe create $ServiceName binPath= $binPath DisplayName= $ServiceDisplayName start= auto | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create service"
        exit 1
    }
    
    # Set service description
    sc.exe description $ServiceName $ServiceDescription | Out-Null
    
    # Configure service recovery (restart on failure)
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    
    # Set service account to LocalSystem
    sc.exe config $ServiceName obj= "LocalSystem" | Out-Null
    
    Write-Host "  Created service: $ServiceName" -ForegroundColor Green
}

# Step 7: Create firewall rule (optional)
if ($CreateFirewallRule) {
    Write-Host "[7/7] Configuring Windows Firewall..." -ForegroundColor Yellow
    try {
        $firewallRuleName = "Openctrol Agent"
        
        # Check if rule exists
        $existingRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
        
        if ($existingRule) {
            # Update existing rule
            Set-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Enabled True | Out-Null
            Write-Host "  Updated firewall rule" -ForegroundColor Green
        } else {
            # Create new rule
            New-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Enabled True | Out-Null
            Write-Host "  Created firewall rule for port $Port" -ForegroundColor Green
        }
    } catch {
        Write-Warning "Failed to configure firewall rule (continuing): $_"
        Write-Warning "You may need to manually allow port $Port in Windows Firewall"
    }
} else {
    Write-Host "[7/7] Skipping firewall configuration (CreateFirewallRule = false)" -ForegroundColor Yellow
}

# Start the service
Write-Host ""
Write-Host "Starting service..." -ForegroundColor Yellow
try {
    Start-Service -Name $ServiceName -ErrorAction Stop
    Start-Sleep -Seconds 3
    
    $service = Get-Service -Name $ServiceName
    if ($service.Status -eq "Running") {
        Write-Host "  Service started successfully" -ForegroundColor Green
    } else {
        Write-Warning "Service is not running. Status: $($service.Status)"
        Write-Warning "Check Event Viewer or logs at $ConfigPath\logs for details"
    }
} catch {
    Write-Error "Failed to start service: $_"
    Write-Warning "Service is installed but not started. You can start it manually with: Start-Service -Name $ServiceName"
    Write-Warning "Check Event Viewer or logs at $ConfigPath\logs for error details"
    exit 1
}

# Success summary
Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Installation Details:" -ForegroundColor Cyan
Write-Host "  Install Folder: $InstallPath" -ForegroundColor White
Write-Host "  Config Folder: $ConfigPath" -ForegroundColor White
Write-Host "  Port: $Port" -ForegroundColor White
Write-Host "  Service: $ServiceName ($ServiceDisplayName)" -ForegroundColor White
Write-Host ""
Write-Host "Health Check:" -ForegroundColor Cyan
$protocol = if ($UseHttps) { "https" } else { "http" }
$hostname = $env:COMPUTERNAME
$healthUrl = "${protocol}://${hostname}:${Port}/api/v1/health"
Write-Host "  $healthUrl" -ForegroundColor White
Write-Host ""
Write-Host "Service Management:" -ForegroundColor Cyan
Write-Host "  Start:   Start-Service -Name $ServiceName" -ForegroundColor White
Write-Host "  Stop:    Stop-Service -Name $ServiceName" -ForegroundColor White
Write-Host "  Status:  Get-Service -Name $ServiceName" -ForegroundColor White
Write-Host "  Logs:    $ConfigPath\logs" -ForegroundColor White
Write-Host ""

exit 0

