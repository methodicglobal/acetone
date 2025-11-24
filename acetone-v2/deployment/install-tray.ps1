#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Installs Acetone V2 Tray Application to Windows Startup

.DESCRIPTION
    Adds the Acetone V2 Tray Application to Windows startup folder

.PARAMETER InstallLocation
    The directory where Acetone is installed. Default: C:\Program Files\Acetone

.PARAMETER RemoveFromStartup
    Remove from startup instead of adding
#>

[CmdletBinding()]
param(
    [string]$InstallLocation = "C:\Program Files\Acetone",
    [switch]$RemoveFromStartup
)

$ErrorActionPreference = "Stop"

$trayExe = Join-Path $InstallLocation "Acetone.TrayApp.exe"
$startupFolder = [System.IO.Path]::Combine(
    [Environment]::GetFolderPath('Startup'),
    "Acetone V2 Proxy.lnk"
)

if ($RemoveFromStartup) {
    Write-Host "Removing Acetone Tray from startup..." -ForegroundColor Yellow
    if (Test-Path $startupFolder) {
        Remove-Item $startupFolder -Force
        Write-Host "✓ Removed from startup" -ForegroundColor Green
    } else {
        Write-Host "Not found in startup" -ForegroundColor Gray
    }
    exit 0
}

if (-not (Test-Path $trayExe)) {
    Write-Error "Tray application not found: $trayExe"
    exit 1
}

Write-Host "Adding Acetone Tray to startup..." -ForegroundColor Yellow

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startupFolder)
$shortcut.TargetPath = $trayExe
$shortcut.WorkingDirectory = $InstallLocation
$shortcut.Description = "Acetone V2 Proxy Manager"
$shortcut.Save()

Write-Host "✓ Added to startup: $startupFolder" -ForegroundColor Green
Write-Host ""
Write-Host "The tray application will start automatically on next login." -ForegroundColor Cyan
Write-Host "To start it now, run: $trayExe" -ForegroundColor Cyan
