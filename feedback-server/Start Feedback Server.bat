@echo off
title ChinaGo Feedback Server
cd /d "%~dp0"

set "PYEXE="
for %%P in (
  "C:\Program Files\Python313\python.exe"
  "C:\Program Files\Python312\python.exe"
  "C:\Program Files\Python311\python.exe"
  "C:\Program Files\Python310\python.exe"
  "C:\Users\Administrator\.workbuddy\binaries\python\versions\3.13.12\python.exe"
) do (
  if not defined PYEXE (
    if exist %%P set "PYEXE=%%~P"
  )
)

if not defined PYEXE (
  for /f "delims=" %%P in ('where python 2^>nul') do (
    if not defined PYEXE set "PYEXE=%%P"
  )
)

if not defined PYEXE (
  echo.
  echo  [ERROR] Python not found on this PC.
  echo.
  echo  Install Python 3 from:  https://www.python.org/downloads/
  echo  Then run this file again.
  echo.
  pause
  exit /b 1
)

echo.
echo  Using Python: %PYEXE%
echo  Feedback folder: D:\ChinaGo\Feedback
echo.

"%PYEXE%" server.py

echo.
echo  Server stopped.
pause
