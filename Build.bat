@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Usage: Build.bat "C:\path\to\game" (or set GAME_ROOT first)
if not "%~1"=="" set "GAME_ROOT=%~1"
if not defined GAME_ROOT (
    echo [ERROR] Game path missing.
    echo Usage: Build.bat "C:\path\to\DIVE or DIE - Children of Rain"
    echo Alternatively, set GAME_ROOT before running this script.
    exit /b 1
)

set "MANAGED_DIR=%GAME_ROOT%\Dive or Die_Data\Managed"
set "CORE_DIR=%GAME_ROOT%\BepInEx\core"
set "OUTPUT_DIR=%~dp0artifacts\Release"

if not exist "%MANAGED_DIR%\Assembly-CSharp.dll" (
    echo [ERROR] Could not find Assembly-CSharp.dll in "%MANAGED_DIR%".
    exit /b 1
)
if not exist "%CORE_DIR%\BepInEx.dll" (
    echo [ERROR] Could not find BepInEx.dll in "%CORE_DIR%".
    echo Install BepInEx 5 and launch the game once.
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK not found on PATH.
    exit /b 1
)

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"
if errorlevel 1 exit /b 1

echo Building DiveOrDieCoop...
dotnet build "%~dp0DiveOrDieCoop.csproj" -c Release -p:GameManagedDir="%MANAGED_DIR%" -p:BepInExCoreDir="%CORE_DIR%" -p:OutDir="%OUTPUT_DIR%/"
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

echo [SUCCESS] Build artifacts: "%OUTPUT_DIR%"
echo Deploy DiveOrDieCoop.dll to your game's BepInEx\plugins directory.
exit /b 0
