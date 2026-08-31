<#
.SYNOPSIS
    Builds ID_Thread_Fix standalone executable.
#>

[CmdletBinding()]
param(
    [switch]$UseDotnet
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$distDir = Join-Path $ScriptDir "dist"
if (!(Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$outputExe = Join-Path $distDir "ID_Thread_Fix.exe"
$icoPath = Join-Path $ScriptDir "assets\app.ico"
$manifestPath = Join-Path $ScriptDir "src\app.manifest"
$resHacker = "C:\Program Files (x86)\Resource Hacker\ResourceHacker.exe"

# Stop any running process to prevent file locks
Get-Process -Name "ID_Thread_Fix", "ID_cpu_2026" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $cscPath)) {
    $cscPath = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$buildSuccess = $false

if (!$UseDotnet -and (Test-Path $cscPath)) {
    Write-Host "[INFO] Building with .NET Framework CSC compiler..." -ForegroundColor Cyan
    $srcFiles = @("$ScriptDir\src\Program.cs", "$ScriptDir\src\AssemblyInfo.cs")

    $argsList = @(
        "/target:winexe",
        "/optimize+",
        "/platform:anycpu",
        "/win32icon:$icoPath",
        "/win32manifest:$manifestPath",
        "/out:$outputExe"
    ) + $srcFiles

    & $cscPath $argsList
    if ($LASTEXITCODE -eq 0) {
        $buildSuccess = $true
    }
}

if (!$buildSuccess) {
    Write-Host "[INFO] Building with dotnet CLI..." -ForegroundColor Cyan
    dotnet build -c Release -f net48
    $dotnetOutput = Join-Path $ScriptDir "bin\Release\net48\ID_Thread_Fix.exe"
    if (Test-Path $dotnetOutput) {
        Copy-Item $dotnetOutput $outputExe -Force
        $buildSuccess = $true
    }
}

if (!$buildSuccess) {
    Write-Error "Build failed!"
    return
}

# Post-build: Resource Hacker icon injection
if (Test-Path $resHacker) {
    Write-Host "[INFO] Injecting icon via Resource Hacker..." -ForegroundColor Cyan
    & $resHacker -open "$outputExe" -save "$outputExe" -action addoverwrite -res "$icoPath" -mask "ICONGROUP,MAINICON," -log CON | Out-Null
    Write-Host "[SUCCESS] Icon injected via Resource Hacker!" -ForegroundColor Green
}

Write-Host "[SUCCESS] Output created at: $outputExe" -ForegroundColor Green
