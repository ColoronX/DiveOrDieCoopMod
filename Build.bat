@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Change this to your DIVE or DIE installation folder.
set "GAME_ROOT=C:\Steam\steamapps\common\DIVE or DIE - Children of Rain"

rem Optional: pass a game directory as the first argument.
if not "%~1"=="" set "GAME_ROOT=%~1"

set "GameManagedDir=%GAME_ROOT%\Dive or Die_Data\Managed"
set "BepInExCoreDir=%GAME_ROOT%\BepInEx\core"
set "OUTPUT_DIR=%~dp0bin\Release"

if not exist "%~dp0DiveOrDieCoop.csproj" (
    echo [ERROR] DiveOrDieCoop.csproj not found next to Build.bat.
    goto :fail
)
if not exist "%GameManagedDir%\Assembly-CSharp.dll" (
    echo [ERROR] Game assembly not found: "%GameManagedDir%\Assembly-CSharp.dll"
    echo Check GAME_ROOT above.
    goto :fail
)
if not exist "%BepInExCoreDir%\BepInEx.dll" (
    echo [ERROR] BepInEx assembly not found: "%BepInExCoreDir%\BepInEx.dll"
    goto :fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet SDK not found in PATH.
    goto :fail
)

echo Building DiveOrDieCoop...
echo Output: "%OUTPUT_DIR%"
echo.

rem The .csproj reads GameManagedDir and BepInExCoreDir from the environment.
rem -o forces the final DLL into bin\Release (not into a game folder).
dotnet build "%~dp0DiveOrDieCoop.csproj" -c Release -o "%OUTPUT_DIR%" --nologo
if errorlevel 1 goto :fail
if not exist "%OUTPUT_DIR%\DiveOrDieCoop.dll" (
    echo [ERROR] Build finished but the DLL was not found in bin\Release.
    goto :fail
)

echo.
echo [SUCCESS] "%OUTPUT_DIR%\DiveOrDieCoop.dll"
pause
exit /b 0

:fail
echo.
echo [ERROR] Compilation failed; see messages above.
pause
exit /b 1
