#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Acetone V2 Proxy Installer for Windows

.DESCRIPTION
    Installs Acetone V2 Proxy and optionally registers it as a Windows Service.

.PARAMETER InstallLocation
    The directory where Acetone will be installed. Default: C:\Program Files\Acetone

.PARAMETER CreateService
    If specified, creates a Windows Service for Acetone V2 Proxy

.PARAMETER ServiceName
    Name of the Windows Service. Default: AcetoneV2Proxy

.PARAMETER Port
    HTTP port for the proxy. Default: 8080

.PARAMETER MetricsPort
    Prometheus metrics port. Default: 9090

.PARAMETER StartService
    If specified, starts the service after installation

.EXAMPLE
    .\install.ps1
    Basic installation

.EXAMPLE
    .\install.ps1 -CreateService -StartService
    Install and create Windows Service, then start it

.EXAMPLE
    .\install.ps1 -InstallLocation "D:\Apps\Acetone" -Port 80 -MetricsPort 9090
    Install to custom location with custom ports
#>

[CmdletBinding()]
param(
    [string]$InstallLocation = "C:\Program Files\Acetone",
    [switch]$CreateService,
    [string]$ServiceName = "AcetoneV2Proxy",
    [int]$Port = 8080,
    [int]$MetricsPort = 9090,
    [switch]$StartService
)

# Requires Administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

$ErrorActionPreference = "Stop"

Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Acetone V2 Proxy - Windows Installer" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Function to check .NET runtime
function Test-DotNetRuntime {
    Write-Host "[1/6] Checking .NET Runtime..." -ForegroundColor Yellow

    $dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetPath) {
        Write-Error ".NET is not installed. Please install .NET 10 Runtime from https://dotnet.microsoft.com/download/dotnet/10.0"
        return $false
    }

    $runtimes = & dotnet --list-runtimes 2>&1
    $hasAspNetCore10 = $runtimes | Select-String "Microsoft.AspNetCore.App 10\."

    if (-not $hasAspNetCore10) {
        Write-Warning "ASP.NET Core 10 Runtime not found!"
        Write-Host "Please install from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Found runtimes:" -ForegroundColor Gray
        $runtimes | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }

        $continue = Read-Host "`nDo you want to continue anyway? (y/N)"
        if ($continue -ne 'y') {
            return $false
        }
    } else {
        Write-Host "  ✓ .NET 10 ASP.NET Core Runtime found" -ForegroundColor Green
    }

    return $true
}

# Function to install files
function Install-Files {
    Write-Host "[2/6] Installing files to $InstallLocation..." -ForegroundColor Yellow

    # Get the current script directory
    $sourceDir = $PSScriptRoot

    # Create installation directory
    if (-not (Test-Path $InstallLocation)) {
        New-Item -ItemType Directory -Path $InstallLocation -Force | Out-Null
        Write-Host "  ✓ Created directory: $InstallLocation" -ForegroundColor Green
    } else {
        Write-Host "  ℹ Directory already exists: $InstallLocation" -ForegroundColor Gray
    }

    # Copy files (exclude install scripts and README from source to avoid overwriting)
    $filesToCopy = Get-ChildItem -Path $sourceDir -File | Where-Object {
        $_.Name -notmatch "^install\.(ps1|sh)$"
    }

    foreach ($file in $filesToCopy) {
        Copy-Item -Path $file.FullName -Destination $InstallLocation -Force
        Write-Host "  ✓ Copied: $($file.Name)" -ForegroundColor Green
    }

    # Copy README and install scripts
    if (Test-Path "$sourceDir\README.md") {
        Copy-Item -Path "$sourceDir\README.md" -Destination $InstallLocation -Force
        Write-Host "  ✓ Copied: README.md" -ForegroundColor Green
    }

    Copy-Item -Path $PSCommandPath -Destination $InstallLocation -Force
    Write-Host "  ✓ Copied: install.ps1" -ForegroundColor Green
}

# Function to create configuration
function New-Configuration {
    Write-Host "[3/6] Configuring application..." -ForegroundColor Yellow

    $configPath = Join-Path $InstallLocation "appsettings.Production.json"

    $config = @{
        Logging = @{
            LogLevel = @{
                Default = "Information"
                "Microsoft.AspNetCore" = "Warning"
                "Yarp" = "Information"
            }
        }
        AllowedHosts = "*"
        Urls = "http://0.0.0.0:$Port"
    } | ConvertTo-Json -Depth 10

    Set-Content -Path $configPath -Value $config -Force
    Write-Host "  ✓ Created configuration: $configPath" -ForegroundColor Green
    Write-Host "  ℹ HTTP Port: $Port" -ForegroundColor Gray
    Write-Host "  ℹ Metrics Port: $MetricsPort" -ForegroundColor Gray
}

# Function to configure firewall
function Set-FirewallRules {
    Write-Host "[4/6] Configuring Windows Firewall..." -ForegroundColor Yellow

    $ruleName = "Acetone V2 Proxy - HTTP"
    $metricsRuleName = "Acetone V2 Proxy - Metrics"

    # Remove existing rules
    Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Get-NetFirewallRule -DisplayName $metricsRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

    # Create new rules
    New-NetFirewallRule -DisplayName $ruleName `
        -Direction Inbound `
        -LocalPort $Port `
        -Protocol TCP `
        -Action Allow `
        -Description "Allow HTTP traffic to Acetone V2 Proxy" | Out-Null
    Write-Host "  ✓ Allowed inbound traffic on port $Port" -ForegroundColor Green

    New-NetFirewallRule -DisplayName $metricsRuleName `
        -Direction Inbound `
        -LocalPort $MetricsPort `
        -Protocol TCP `
        -Action Allow `
        -Description "Allow Prometheus metrics access for Acetone V2 Proxy" | Out-Null
    Write-Host "  ✓ Allowed inbound traffic on port $MetricsPort (metrics)" -ForegroundColor Green
}

# Function to create Windows Service
function New-WindowsService {
    Write-Host "[5/6] Creating Windows Service..." -ForegroundColor Yellow

    $exePath = Join-Path $InstallLocation "Acetone.V2.Proxy.exe"

    if (-not (Test-Path $exePath)) {
        Write-Error "Executable not found: $exePath"
        return $false
    }

    # Check if service already exists
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "  ℹ Service '$ServiceName' already exists. Stopping and removing..." -ForegroundColor Gray
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName
        Start-Sleep -Seconds 2
    }

    # Create service
    & sc.exe create $ServiceName `
        binPath= "`"$exePath`"" `
        start= auto `
        DisplayName= "Acetone V2 Reverse Proxy" `
        depend= "HTTP"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create service"
        return $false
    }

    # Set description
    & sc.exe description $ServiceName "High-performance reverse proxy built on YARP and .NET 10"

    # Set recovery options (restart on failure)
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

    Write-Host "  ✓ Windows Service '$ServiceName' created successfully" -ForegroundColor Green

    # Set environment variables for the service
    $envVars = @(
        "ASPNETCORE_ENVIRONMENT=Production"
        "ASPNETCORE_URLS=http://0.0.0.0:$Port"
    )

    # Note: Setting environment variables for services requires registry modification
    $servicePath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (Test-Path $servicePath) {
        Set-ItemProperty -Path $servicePath -Name "Environment" -Value $envVars -Type MultiString -ErrorAction SilentlyContinue
        Write-Host "  ✓ Service environment configured" -ForegroundColor Green
    }

    return $true
}

# Function to start service
function Start-AcetoneService {
    Write-Host "[6/6] Starting service..." -ForegroundColor Yellow

    try {
        Start-Service -Name $ServiceName
        Start-Sleep -Seconds 3

        $service = Get-Service -Name $ServiceName
        if ($service.Status -eq 'Running') {
            Write-Host "  ✓ Service started successfully" -ForegroundColor Green
        } else {
            Write-Warning "Service status: $($service.Status)"
        }
    } catch {
        Write-Error "Failed to start service: $_"
        Write-Host "  ℹ Check Event Viewer for details: eventvwr.msc" -ForegroundColor Yellow
    }
}

# Main installation process
try {
    # Step 1: Check .NET Runtime
    if (-not (Test-DotNetRuntime)) {
        exit 1
    }

    # Step 2: Install Files
    Install-Files

    # Step 3: Create Configuration
    New-Configuration

    # Step 4: Configure Firewall
    Set-FirewallRules

    # Step 5: Create Service (if requested)
    if ($CreateService) {
        if (-not (New-WindowsService)) {
            Write-Warning "Service creation failed, but files were installed successfully"
        }
    } else {
        Write-Host "[5/6] Skipping service creation (use -CreateService to enable)" -ForegroundColor Gray
        Write-Host "[6/6] Skipping service start" -ForegroundColor Gray
    }

    # Step 6: Start Service (if requested and created)
    if ($CreateService -and $StartService) {
        Start-AcetoneService
    } elseif ($CreateService) {
        Write-Host "[6/6] Service created but not started (use -StartService to auto-start)" -ForegroundColor Gray
    }

    # Success message
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  ✓ Installation completed successfully!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installation Location: $InstallLocation" -ForegroundColor Cyan
    Write-Host "HTTP Port: $Port" -ForegroundColor Cyan
    Write-Host "Metrics Port: $MetricsPort" -ForegroundColor Cyan

    if ($CreateService) {
        Write-Host "Service Name: $ServiceName" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Service Management Commands:" -ForegroundColor Yellow
        Write-Host "  Start:   Start-Service $ServiceName" -ForegroundColor Gray
        Write-Host "  Stop:    Stop-Service $ServiceName" -ForegroundColor Gray
        Write-Host "  Restart: Restart-Service $ServiceName" -ForegroundColor Gray
        Write-Host "  Status:  Get-Service $ServiceName" -ForegroundColor Gray
    } else {
        Write-Host ""
        Write-Host "To run manually:" -ForegroundColor Yellow
        Write-Host "  cd `"$InstallLocation`"" -ForegroundColor Gray
        Write-Host "  .\Acetone.V2.Proxy.exe" -ForegroundColor Gray
        Write-Host ""
        Write-Host "To create a service later, run:" -ForegroundColor Yellow
        Write-Host "  .\install.ps1 -CreateService -StartService" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "Health Check: http://localhost:$Port/health" -ForegroundColor Yellow
    Write-Host "Metrics: http://localhost:$MetricsPort/metrics" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Documentation: See README.md in $InstallLocation" -ForegroundColor Cyan

} catch {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "  ✗ Installation failed!" -ForegroundColor Red
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""
    exit 1
}
