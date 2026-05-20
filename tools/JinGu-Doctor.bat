@echo off
title JinGu Cheats - Mod Doctor

REM Self-elevate to admin (Program Files writes need it for the repair step)
fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator privileges...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0jingu-doctor.ps1"
