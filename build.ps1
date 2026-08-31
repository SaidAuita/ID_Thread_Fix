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

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $cscPath)) {
    $cscPath = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (!$UseDotnet -and (Test-Path $cscPath)) {
    Write-Host "[INFO] Building with .NET Framework CSC compiler..." -ForegroundColor Cyan
    $outputExe = Join-Path $distDir "ID_Thread_Fix.exe"
    $srcFiles = @(Join-Path $ScriptDir "src\Program.cs", Join-Path $ScriptDir "src\AssemblyInfo.cs")
    $icoPath = Join-Path $ScriptDir "assets\app.ico"
    $manifestPath = Join-Path $ScriptDir "src\app.manifest"

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
        Write-Host "[SUCCESS] Output created at: $outputExe" -ForegroundColor Green
        return
    }
}

Write-Host "[INFO] Building with dotnet CLI..." -ForegroundColor Cyan
dotnet build -c Release -f net48
$dotnetOutput = Join-Path $ScriptDir "bin\Release\net48\ID_Thread_Fix.exe"
if (Test-Path $dotnetOutput) {
    Copy-Item $dotnetOutput (Join-Path $distDir "ID_Thread_Fix.exe") -Force
    Write-Host "[SUCCESS] Output created at: $(Join-Path $distDir "ID_Thread_Fix.exe")" -ForegroundColor Green
} else {
    Write-Error "Build failed!"
}
