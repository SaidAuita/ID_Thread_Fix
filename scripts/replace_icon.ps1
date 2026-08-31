<#
.SYNOPSIS
    Replaces application icon in ID_Thread_Fix.exe using original Adobe InDesign ICON1_1.ico via Resource Hacker.
#>

[CmdletBinding()]
param(
    [string]$ExePath = "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe",
    [string]$IcoPath = "C:\_CODE\indesign\ID_cpu\InDesign_icons\ICON1_1.ico",
    [string]$ResHackerPath = "C:\Program Files (x86)\Resource Hacker\ResourceHacker.exe"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $IcoPath)) {
    # Fallback to local assets\app.ico if external folder not found
    $IcoPath = "C:\_CODE\indesign\ID_Thread_Fix\assets\app.ico"
}

if (!(Test-Path $ResHackerPath)) {
    Write-Error "Resource Hacker not found at: $ResHackerPath"
    return
}

if (!(Test-Path $ExePath)) {
    Write-Error "Target EXE not found at: $ExePath"
    return
}

if (!(Test-Path $IcoPath)) {
    Write-Error "Source ICO not found at: $IcoPath"
    return
}

Write-Host "[INFO] Injecting original Adobe InDesign icon into $ExePath via Resource Hacker..." -ForegroundColor Cyan

# Stop any running instances to prevent file lock
Get-Process -Name "ID_Thread_Fix", "ID_cpu_2026" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Create backup
$backupPath = [System.IO.Path]::ChangeExtension($ExePath, "_backup.exe")
Copy-Item $ExePath $backupPath -Force

& $ResHackerPath `
  -open "$ExePath" `
  -save "$ExePath" `
  -action addoverwrite `
  -res "$IcoPath" `
  -mask "ICONGROUP,MAINICON," `
  -log CON

if ($LASTEXITCODE -eq 0) {
    Write-Host "[SUCCESS] Icon successfully replaced with original Adobe ICON1_1.ico!" -ForegroundColor Green
} else {
    Write-Error "Resource Hacker failed with code $LASTEXITCODE"
}
