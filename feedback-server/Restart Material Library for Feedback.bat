@echo off
title ChinaGo Feedback - Restart Material Library
cd /d "D:\Program Files (x86)\MaterialLibrary\server"

echo ==========================================================
echo    ChinaGo Feedback  -  Restart Material Library
echo ==========================================================
echo.
echo    The feedback patch is already installed on disk.
echo    This restarts the Material Library server so that it
echo    loads the new feedback module.
echo.
echo    IMPACT:  colleagues lose access for about 5 seconds.
echo             The guard.bat watchdog restarts it automatically.
echo.
echo    Tip: run this during a break if colleagues are using it.
echo.
echo    Press any key to continue, or just close this window.
echo.
pause >nul

echo.
echo  [1/3] Stopping Material Library server ...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":3000 " ^| findstr LISTENING') do (
  echo         killing PID %%a
  taskkill /F /PID %%a >nul 2>&1
)

echo  [2/3] Waiting for guard.bat to restart it (8s) ...
timeout /t 8 >nul

netstat -ano | findstr ":3000 " | findstr LISTENING >nul
if errorlevel 1 (
  echo         guard not running - starting it manually ...
  cscript //nologo "start-hidden.vbs" >nul 2>&1
  timeout /t 5 >nul
)

echo  [3/3] Opening health check in your browser ...
start "" "http://127.0.0.1:3000/chinago/api/health"

echo.
echo ----------------------------------------------------------
echo    A browser window just opened. Check what it shows:
echo.
echo    OK    :  {"ok":true,"service":"chinago-feedback",...}
echo            ^^- feedback is LIVE, nothing else to do.
echo.
echo    FAIL  :  page cannot be displayed, or "Not Found"
echo            ^^- run "Rollback Feedback Patch.bat" now.
echo.
echo    Feedback is saved to:   D:\ChinaGo\Feedback\
echo    Open the summary page:  D:\ChinaGo\Feedback\index.html
echo ----------------------------------------------------------
echo.
pause
