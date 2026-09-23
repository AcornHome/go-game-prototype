@echo off
setlocal
cd /d "%~dp0"
set "VER=1.5.3"
set "SRC=dist"
set "OUT=ChinaGo-%VER%-win.zip"
if not exist "%SRC%" (
  echo [ERROR] %SRC% not found - run Build first
  pause
  exit /b 1
)
if exist "%OUT%" del "%OUT%"
powershell -NoProfile -Command "Compress-Archive -Path '%SRC%\*' -DestinationPath '%OUT%' -Force"
if not exist "%OUT%" (
  echo [ERROR] zip failed
  pause
  exit /b 1
)
echo.
echo DONE: %CD%\%OUT%
echo.
pause
