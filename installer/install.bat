@echo off
setlocal enabledelayedexpansion
title JinGu Cheats Installer

REM Self-elevate to admin if not already (writing to Program Files needs it)
fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator privileges...
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0"

echo.
echo =========================================================
echo   JinGu Cheats Installer
echo =========================================================
echo.
echo Antivirus note:
echo   Windows Defender and other AV products often flag game-cheat
echo   DLLs because they hook into another process. The full source
echo   is on GitHub if you want to inspect before installing. If your
echo   AV blocks any file, allow it or add an exclusion.
echo.
pause

REM === Step 1: detect game folder ===
set "GAME_DIR="
set "STEAM_DEFAULT=C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu"
if exist "%STEAM_DEFAULT%\JinGu.exe" (
  set "GAME_DIR=%STEAM_DEFAULT%"
  echo Found JinGu at default Steam location:
  echo   !GAME_DIR!
)

if "%GAME_DIR%"=="" (
  echo Could not find JinGu at the default Steam location.
  echo.
  echo Please paste the full path to your JinGu folder
  echo ^(the folder that contains JinGu.exe^):
  set /p "GAME_DIR=Path: "
)

REM Strip surrounding quotes if user pasted them
set "GAME_DIR=!GAME_DIR:"=!"

if not exist "!GAME_DIR!\JinGu.exe" (
  echo.
  echo ERROR: !GAME_DIR! does not contain JinGu.exe.
  echo Please re-run and enter the correct path.
  pause
  exit /b 1
)

echo.
echo Installing to: !GAME_DIR!
echo.

REM === Step 2: check MelonLoader is installed ===
if not exist "!GAME_DIR!\version.dll" (
  echo.
  echo MelonLoader is not installed yet. It is required for this mod.
  echo.
  echo Opening the MelonLoader download page in your browser...
  echo.
  echo Steps:
  echo   1. Download MelonLoader.x64.zip from the page that just opened
  echo   2. Extract its contents directly into:
  echo      !GAME_DIR!
  echo      ^(version.dll should end up next to JinGu.exe^)
  echo   3. Re-run this installer
  echo.
  start "" "https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3"
  pause
  exit /b 1
)
echo MelonLoader detected.

REM === Step 3: patch stripped Mono runtime ===
echo Patching stripped Mono runtime...
set "PATCH_SRC=!GAME_DIR!\MelonLoader\Dependencies\MonoBleedingEdgePatches"
set "PATCH_DST=!GAME_DIR!\JinGu_Data\Managed"

if not exist "!PATCH_SRC!\mscorlib.dll" (
  echo.
  echo ERROR: MelonLoader's MonoBleedingEdgePatches folder is missing:
  echo   !PATCH_SRC!
  echo Your MelonLoader install may be incomplete. Re-extract MelonLoader.x64.zip.
  pause
  exit /b 1
)

for %%F in (mscorlib.dll System.dll System.Core.dll System.Runtime.dll) do (
  attrib -R "!PATCH_DST!\%%F" >nul 2>&1
  copy /Y "!PATCH_SRC!\%%F" "!PATCH_DST!\%%F" >nul
  if errorlevel 1 (
    echo ERROR: could not write !PATCH_DST!\%%F. Try running as admin.
    pause
    exit /b 1
  )
)
echo Corlibs patched.

REM === Step 4: copy mod files to Mods\ ===
echo Copying mod files...
if not exist "!GAME_DIR!\Mods" mkdir "!GAME_DIR!\Mods"

set "MISSING=0"
for %%F in (JinGuCheats.dll jingu-cheats-ui.exe pre-launch.bat pre-launch.ps1) do (
  if not exist "%~dp0%%F" (
    echo ERROR: %%F is missing from the installer folder.
    set "MISSING=1"
  )
)
if "!MISSING!"=="1" (
  echo Re-extract the release zip and try again.
  pause
  exit /b 1
)

copy /Y "%~dp0JinGuCheats.dll"     "!GAME_DIR!\Mods\" >nul
copy /Y "%~dp0jingu-cheats-ui.exe" "!GAME_DIR!\Mods\" >nul
copy /Y "%~dp0pre-launch.bat"      "!GAME_DIR!\Mods\" >nul
copy /Y "%~dp0pre-launch.ps1"      "!GAME_DIR!\Mods\" >nul

echo.
echo =========================================================
echo   Installation complete!
echo =========================================================
echo.
echo Recommended: add this Steam Launch Option to auto-heal
echo future game updates ^(some Steam updates revert the corlibs
echo we just patched, which silently breaks the mod^):
echo.
echo   "!GAME_DIR!\Mods\pre-launch.bat" %%command%%
echo.
echo Set it in: Steam ^-^> JinGu ^-^> Properties ^-^> General ^-^> Launch Options
echo.
echo Launch JinGu through Steam. The cheat UI window opens
echo automatically a couple seconds after the game starts.
echo.
pause
endlocal
