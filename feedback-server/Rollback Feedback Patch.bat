@echo off
title ChinaGo Feedback - Rollback Patch
cd /d "D:\Program Files (x86)\MaterialLibrary\server"

echo ==========================================================
echo    ChinaGo Feedback  -  Rollback Patch
echo ==========================================================
echo.
echo    This restores the ORIGINAL server.js
echo    (from backup  server.js.bak-20260910)
echo    and deletes the feedback module.
echo.
echo    Use this if the Material Library misbehaves after
echo    the restart. Everything goes back to how it was.
echo.
echo    Press any key to continue, or just close this window.
echo.
pause >nul

if not exist "server.js.bak-20260910" (
  echo.
  echo   [ERROR] Backup file not found: server.js.bak-20260910
  echo           Cannot rollback. Call the developer.
  echo.
  pause
  exit /b 1
)

echo.
echo  [1/3] Restoring server.js ...
copy /Y "server.js.bak-20260910" "server.js" >nul
echo         done.

echo  [2/3] Removing chinago-feedback.js ...
del /Q "chinago-feedback.js" >nul 2>&1
echo         done.

echo  [3/3] Restarting Material Library server ...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":3000 " ^| findstr LISTENING') do taskkill /F /PID %%a >nul 2>&1
timeout /t 8 >nul

netstat -ano | findstr ":3000 " | findstr LISTENING >nul
if errorlevel 1 (
  cscript //nologo "start-hidden.vbs" >nul 2>&1
  timeout /t 5 >nul
)

start "" "http://127.0.0.1:3000/"
echo.
echo ----------------------------------------------------------
echo    Done. A browser window opened the Material Library.
echo.
echo    If it loads normally, the rollback succeeded and
echo    the Material Library is exactly as before.
echo ----------------------------------------------------------
echo.
pause
