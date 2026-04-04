# CXPost Uninstaller for Windows
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

$installDir = "$env:LOCALAPPDATA\CXPost"
$configDir = "$env:APPDATA\CXPost"

Write-Host "CXPost Uninstaller" -ForegroundColor Cyan
Write-Host ""

# Remove binary
if (Test-Path "$installDir\cxpost.exe") {
    Remove-Item "$installDir\cxpost.exe" -Force
    Write-Host "Removed $installDir\cxpost.exe" -ForegroundColor Green
} else {
    Write-Host "Binary not found at $installDir\cxpost.exe"
}

# Ask about config
if (Test-Path $configDir) {
    Write-Host ""
    Write-Host "Config directory: $configDir"
    Write-Host "  Contains: config.yaml, credentials"
    $confirm = Read-Host "  Remove config? [y/N]"
    if ($confirm -eq 'y' -or $confirm -eq 'Y') {
        Remove-Item $configDir -Recurse -Force
        Write-Host "  Removed $configDir" -ForegroundColor Green
    } else {
        Write-Host "  Kept $configDir"
    }
}

# Ask about data
$dataDir = "$env:LOCALAPPDATA\CXPost\data"
if (Test-Path $dataDir) {
    Write-Host ""
    Write-Host "Data directory: $dataDir"
    Write-Host "  Contains: mail.db, contacts.db (cached emails)"
    $confirm = Read-Host "  Remove data? [y/N]"
    if ($confirm -eq 'y' -or $confirm -eq 'Y') {
        Remove-Item $dataDir -Recurse -Force
        Write-Host "  Removed $dataDir" -ForegroundColor Green
    } else {
        Write-Host "  Kept $dataDir"
    }
}

# Remove install dir if empty
if ((Test-Path $installDir) -and (Get-ChildItem $installDir | Measure-Object).Count -eq 0) {
    Remove-Item $installDir -Force
}

# Remove from PATH
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -like "*$installDir*") {
    $newPath = ($userPath -split ';' | Where-Object { $_ -ne $installDir }) -join ';'
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host ""
    Write-Host "Removed $installDir from PATH" -ForegroundColor Green
}

Write-Host ""
Write-Host "CXPost uninstalled." -ForegroundColor Green
