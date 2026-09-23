@echo off
title Go Game Setup
setlocal

REM Switch to script directory
set "WORKDIR=%~dp0"
cd /d "%WORKDIR%"

REM Hand off to PowerShell
powershell -NoProfile -ExecutionPolicy Bypass -File "%WORKDIR%setup.ps1"

endlocal