# CXPost Installer for Windows
# Usage: irm https://raw.githubusercontent.com/nickprotop/cxpost/main/install.ps1 | iex
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

$ErrorActionPreference = "Stop"

$repo = "nickprotop/cxpost"
$installDir = "$env:LOCALAPPDATA\CXPost"
$configDir = "$env:APPDATA\CXPost"

Write-Host "Installing CXPost..." -ForegroundColor Cyan

# Detect architecture
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($arch) {
    "X64"   { $binary = "cxpost-win-x64.exe" }
    "Arm64" { $binary = "cxpost-win-arm64.exe" }
    default {
        Write-Host "Error: Unsupported architecture: $arch" -ForegroundColor Red
        exit 1
    }
}

# Get latest release
Write-Host "Fetching latest release..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest"
$version = $release.tag_name -replace '^v', ''
$asset = $release.assets | Where-Object { $_.name -eq $binary }

if (-not $asset) {
    Write-Host "Error: Binary '$binary' not found in release $($release.tag_name)" -ForegroundColor Red
    exit 1
}

Write-Host "Latest version: $version"

# Create directories
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
New-Item -ItemType Directory -Force -Path $configDir | Out-Null

# Download binary
Write-Host "Downloading $binary..."
$outputPath = Join-Path $installDir "cxpost.exe"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $outputPath

# Add to PATH if not already there
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$installDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$installDir;$userPath", "User")
    Write-Host "Added $installDir to user PATH" -ForegroundColor Green
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  CXPost v$version installed!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Binary:  $outputPath"
Write-Host "  Config:  $configDir\"
Write-Host ""
Write-Host "  Run:     cxpost"
Write-Host "  Remove:  Remove-Item '$installDir' -Recurse"
Write-Host ""
Write-Host "  Note: Restart your terminal for PATH changes to take effect." -ForegroundColor Yellow
