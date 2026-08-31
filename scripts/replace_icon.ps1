<#
.SYNOPSIS
    Replaces application icon in ID_Thread_Fix.exe using Resource Hacker.
#>

[CmdletBinding()]
param(
    [string]$ExePath = "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe",
    [string]$IcoPath = "C:\_CODE\indesign\ID_Thread_Fix\assets\app.ico",
    [string]$ResHackerPath = "C:\Program Files (x86)\Resource Hacker\ResourceHacker.exe"
)

$ErrorActionPreference = "Stop"

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

Write-Host "[INFO] Injecting icon into $ExePath using Resource Hacker..." -ForegroundColor Cyan

# Stop any running instances to prevent file lock
Get-Process -Name "ID_Thread_Fix", "ID_cpu_2026" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

& $ResHackerPath `
  -open "$ExePath" `
  -save "$ExePath" `
  -action addoverwrite `
  -res "$IcoPath" `
  -mask "ICONGROUP,MAINICON," `
  -log CON

if ($LASTEXITCODE -eq 0) {
    Write-Host "[SUCCESS] Icon successfully replaced in $ExePath" -ForegroundColor Green
} else {
    Write-Error "Resource Hacker failed with code $LASTEXITCODE"
}
