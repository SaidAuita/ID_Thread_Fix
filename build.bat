@echo off
setlocal enabledelayedexpansion
title Building ID_Thread_Fix...

echo =======================================================
echo          Building InDesign Thread Fix (ID_Thread_Fix)
echo =======================================================

set CSC_PATH="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC_PATH% (
    set CSC_PATH="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist "dist" mkdir "dist"

if exist %CSC_PATH% (
    echo [INFO] Compiling using .NET Framework CSC compiler...
    %CSC_PATH% /target:winexe /optimize+ /platform:anycpu /win32icon:assets\app.ico /win32manifest:src\app.manifest /out:dist\ID_Thread_Fix.exe src\Program.cs src\AssemblyInfo.cs
    if !ERRORLEVEL! EQU 0 (
        echo [SUCCESS] dist\ID_Thread_Fix.exe built successfully!
        echo.
        goto DONE
    )
)

echo [INFO] Falling back to dotnet build...
where dotnet >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    dotnet build -c Release -f net48
    if exist "bin\Release\net48\ID_Thread_Fix.exe" (
        copy "bin\Release\net48\ID_Thread_Fix.exe" "dist\ID_Thread_Fix.exe" >nul
        echo [SUCCESS] dist\ID_Thread_Fix.exe built successfully with dotnet!
        goto DONE
    )
)

echo [ERROR] Build failed! Please ensure .NET Framework or .NET SDK is installed.
exit /b 1

:DONE
echo =======================================================
echo  Output Binary: dist\ID_Thread_Fix.exe
echo =======================================================
endlocal
