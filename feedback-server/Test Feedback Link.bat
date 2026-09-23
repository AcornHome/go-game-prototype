@echo off
title ChinaGo - Test Feedback Link
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
  echo  Install Python 3 from:  https://www.python.org/downloads/
  echo.
  pause
  exit /b 1
)

echo.
echo  Testing the feedback link...
echo  A result page will open in your browser.
echo.

"%PYEXE%" test_link.py

echo.
echo  Done. See the page that opened in your browser.
echo.
pause
