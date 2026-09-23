@echo off
setlocal
cd /d "%~dp0"
set "BATDIR=%~dp0"

REM --- make sure the build output exists ---
if not exist "dist" (
  echo [ERROR] dist\ folder not found.
  echo   Run "Build v1.5.3.bat" then "pack-itch.bat" first.
  echo.
  pause
  exit /b 1
)

REM --- locate butler.exe (next to this script, or on PATH) ---
set "BUTLER="
if exist "%BATDIR%butler.exe" set "BUTLER=%BATDIR%butler.exe"
if "%BUTLER%"=="" (
  where butler >nul 2>&1 && set "BUTLER=butler"
)
if "%BUTLER%"=="" (
  echo [ERROR] butler.exe not found.
  echo.
  echo  How to get it (one time only):
  echo   1. Open: https://itch.io/docs/butler/installing.html
  echo   2. Download the Windows version, unzip it.
  echo   3. Copy butler.exe into this folder:
  echo      %BATDIR%
  echo   4. Run this script again.
  echo.
  start "" "https://itch.io/docs/butler/installing.html"
  pause
  exit /b 1
)

REM --- login (first time opens a browser; later it is instant) ---
echo Logging in to itch.io ...
"%BUTLER%" login
if errorlevel 1 (
  echo [ERROR] login failed. Try again.
  pause
  exit /b 1
)

REM --- ask for your itch.io username ---
set /p USER="Enter your itch.io username: "
if "%USER%"=="" (
  echo [ERROR] username was empty.
  pause
  exit /b 1
)

REM --- upload ---
echo.
echo Pushing dist\ to %USER%/chinago:windows-stable ...
"%BUTLER%" push dist "%USER%/chinago:windows-stable" --user "%USER%"
if errorlevel 1 (
  echo [ERROR] push failed. See the messages above.
  echo   Common cause: your itch.io project page URL is not "itch.io/%USER%/chinago".
  echo   Create the project first, or tell me to change the "chinago" name.
  pause
  exit /b 1
)

echo.
echo DONE. Your game is live on itch.io under the windows-stable channel.
echo.
pause
