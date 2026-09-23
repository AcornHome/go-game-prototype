@echo off
setlocal
title ChinaGo 1.5.3 - Build and Package
cd /d "%~dp0"

set "VER=1.5.3"
set "LOGDIR=%~dp0build-logs"
if not exist "%LOGDIR%" md "%LOGDIR%"
set "L1=%LOGDIR%\1-restore.log"
set "L2=%LOGDIR%\2-build.log"
set "L3=%LOGDIR%\3-deploy.log"
set "L4=%LOGDIR%\4-innosetup.log"
set "OUT=installer\ChinaGoSetup-%VER%.exe"
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo.
echo  ==========================================
echo   ChinaGo %VER%   build + package
echo   log folder: %LOGDIR%
echo  ==========================================
echo.

rem ---------- locate python (for deploy.py) ----------
set "PYEXE="
for %%P in (
  "C:\Program Files\Python313\python.exe"
  "C:\Program Files\Python312\python.exe"
  "C:\Program Files\Python311\python.exe"
  "C:\Users\Administrator\.workbuddy\binaries\python\versions\3.13.12\python.exe"
) do (
  if not defined PYEXE (
    if exist %%P set "PYEXE=%%~P"
  )
)

echo [1/4] dotnet restore ...
dotnet restore desktop > "%L1%" 2>&1
if errorlevel 1 goto fail_restore

echo [2/4] dotnet build  Release  v%VER% ...
dotnet build desktop -c Release --no-restore -p:Version=%VER% > "%L2%" 2>&1
if errorlevel 1 goto fail_build

echo [3/4] sync files to dist ...
if not defined PYEXE goto nopython
"%PYEXE%" ".tools\deploy.py" > "%L3%" 2>&1
if errorlevel 1 goto fail_deploy
goto step4

:nopython
echo   [WARN] python not found - keep existing dist folder

:step4
echo [4/4] build installer (Inno Setup) ...
if not exist "%ISCC%" goto noinno
"%ISCC%" "installer\GoSmart.iss" > "%L4%" 2>&1
if errorlevel 1 goto fail_inno

if not exist "%OUT%" goto fail_inno

echo.
echo  ==========================================
echo   DONE - installer created:
echo   %CD%\%OUT%
echo  ==========================================
echo.
start "" explorer /select,"%CD%\%OUT%"
pause
exit /b 0

:fail_restore
echo.
echo  [FAILED] dotnet restore
echo  log: %L1%
type "%L1%"
echo.
pause
exit /b 1

:fail_build
echo.
echo  [FAILED] dotnet build
echo  log: %L2%
type "%L2%"
echo.
pause
exit /b 1

:fail_deploy
echo.
echo  [FAILED] deploy.py - see log:
echo  %L3%
type "%L3%"
echo.
pause
exit /b 1

:fail_inno
echo.
echo  [FAILED] Inno Setup - see log:
echo  %L4%
type "%L4%"
echo.
pause
exit /b 1

:noinno
echo.
echo  [ERROR] Inno Setup 6 not found:
echo  %ISCC%
echo.
pause
exit /b 1
