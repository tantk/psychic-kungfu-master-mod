@echo off
REM Steam launch wrapper. Configure Steam Launch Options to:
REM   "C:\dev\physickungfu_cheat\tools\pre-launch.bat" %command%
REM This calls our PowerShell validator silently, then chains to the real game exe.

powershell -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File "%~dp0pre-launch.ps1"
%*
