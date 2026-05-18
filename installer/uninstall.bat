@echo off
setlocal enabledelayedexpansion
title JinGu Cheats Uninstaller

REM Self-elevate to admin (Program Files writes need it)
fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator privileges...
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0"

echo.
echo =========================================================
echo   JinGu Cheats Uninstaller
echo =========================================================
echo.

REM Detect game folder
set "GAME_DIR="
set "STEAM_DEFAULT=C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu"
if exist "%STEAM_DEFAULT%\JinGu.exe" set "GAME_DIR=%STEAM_DEFAULT%"

if "%GAME_DIR%"=="" (
  echo Could not find JinGu at the default Steam location.
  echo Please paste the full path to your JinGu folder:
  set /p "GAME_DIR=Path: "
)

set "GAME_DIR=!GAME_DIR:"=!"

if not exist "!GAME_DIR!\JinGu.exe" (
  echo ERROR: !GAME_DIR! does not contain JinGu.exe.
  pause
  exit /b 1
)

echo This will remove from !GAME_DIR!:
echo   - version.dll        ^(MelonLoader bootstrap^)
echo   - MelonLoader\       ^(loader + logs^)
echo   - Mods\              ^(plugin + UI + pre-launch scripts^)
echo   - UserData\          ^(MelonLoader prefs + our mod's settings^)
echo.
echo Your SAVE FILES are NOT touched ^(they live in
echo %%LOCALAPPDATA%%Low\..\^).
echo.
set /p "CONFIRM=Continue? (y/N): "
if /i not "!CONFIRM!"=="y" (
  echo Cancelled.
  pause
  exit /b 0
)

REM Stop any running UI process so its exe isn't locked
taskkill /f /im jingu-cheats-ui.exe >nul 2>&1

del /f /q "!GAME_DIR!\version.dll" >nul 2>&1
rd /s /q "!GAME_DIR!\MelonLoader" >nul 2>&1
rd /s /q "!GAME_DIR!\Mods" >nul 2>&1
rd /s /q "!GAME_DIR!\UserData" >nul 2>&1

echo.
echo =========================================================
echo   Mod uninstalled.
echo =========================================================
echo.
echo The Mono runtime ^(mscorlib.dll etc.^) is still in its
echo patched state. To restore the original stripped versions:
echo   Steam ^-^> JinGu ^-^> Properties ^-^> Installed Files
echo   ^-^> Verify integrity of game files
echo Steam will redownload the stripped originals automatically.
echo.
echo If you set a Steam Launch Option for auto-heal, clear it
echo manually in Properties ^-^> General ^-^> Launch Options.
echo.
pause
endlocal
